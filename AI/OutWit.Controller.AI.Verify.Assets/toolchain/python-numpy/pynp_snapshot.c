/* The entry points of the numpy runtime's start-up snapshot (snapshot.sh).
 *
 * The module is linked as a WASI reactor with this file in place of Programs/python.c, so the
 * constructors run once, wherever the module starts:
 *
 *   wizer-initialize  run by the pinned wasmtime's wizer at build time: the interpreter is
 *                     initialized with the sandbox's own command line (-P -B -c) and environment,
 *                     numpy is imported, and the memory is written into the module
 *   _start            what the sandbox calls, as for every module. In the snapshot it runs the
 *                     task's code (-P -B -c <code> [args]) in the interpreter that is already up;
 *                     in the module before wizer it is CPython's own main
 *
 * The sandbox needs no change: it calls _start, never _initialize. The snapshot carries what
 * wizer saw: the environment, the preopened "/" (fd 3) and the imported modules. A task sees no
 * other difference than numpy being in sys.modules already (not in its namespace).
 *
 * The same bytes from every build: while s_pinned is set (from the first constructor to the end
 * of wizer-initialize, and in a plain run until _start begins) the clocks answer
 * SOURCE_DATE_EPOCH or 0, entropy answers zeros, and file metadata keeps size and type with no
 * device, inode or times. snapshot.sh links with --wrap for the four calls. The approach is
 * wasi-python's (Apache-2.0, github.com/ClayGendron/wasi_python, recipes/reactor_main.c). */
#include <Python.h>

#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <wasi/api.h>

#ifndef PYNP_SOURCE_DATE_EPOCH
#error "PYNP_SOURCE_DATE_EPOCH must be defined (snapshot.sh)"
#endif

extern void __wasm_call_ctors(void);

static int s_constructed;
static int s_initialized;
static volatile int s_pinned = 1;

__attribute__((constructor)) static void pynp_mark_constructed(void) {
    s_constructed = 1;
}

/* Wizer may or may not call the reactor's _initialize before wizer-initialize; either way the
 * constructors run once. */
static void construct(void) {
    if (!s_constructed)
        __wasm_call_ctors();
}

__wasi_errno_t __real___wasi_clock_time_get(__wasi_clockid_t id, __wasi_timestamp_t precision, __wasi_timestamp_t *time);
__wasi_errno_t __real___wasi_random_get(uint8_t *buffer, __wasi_size_t length);
__wasi_errno_t __real___wasi_path_filestat_get(__wasi_fd_t fd, __wasi_lookupflags_t flags, const char *path, __wasi_filestat_t *stat);
__wasi_errno_t __real___wasi_fd_filestat_get(__wasi_fd_t fd, __wasi_filestat_t *stat);

__wasi_errno_t __wrap___wasi_clock_time_get(__wasi_clockid_t id, __wasi_timestamp_t precision, __wasi_timestamp_t *time) {
    if (!s_pinned)
        return __real___wasi_clock_time_get(id, precision, time);
    *time = id == __WASI_CLOCKID_REALTIME ? (__wasi_timestamp_t)PYNP_SOURCE_DATE_EPOCH * 1000000000ull : 0;
    return __WASI_ERRNO_SUCCESS;
}

__wasi_errno_t __wrap___wasi_random_get(uint8_t *buffer, __wasi_size_t length) {
    if (!s_pinned)
        return __real___wasi_random_get(buffer, length);
    memset(buffer, 0, length);
    return __WASI_ERRNO_SUCCESS;
}

static void pin_stat(__wasi_filestat_t *stat) {
    stat->dev = 0;
    stat->ino = 0;
    stat->nlink = 1;
    stat->atim = stat->mtim = stat->ctim = (__wasi_timestamp_t)PYNP_SOURCE_DATE_EPOCH * 1000000000ull;
}

__wasi_errno_t __wrap___wasi_path_filestat_get(__wasi_fd_t fd, __wasi_lookupflags_t flags, const char *path, __wasi_filestat_t *stat) {
    __wasi_errno_t error = __real___wasi_path_filestat_get(fd, flags, path, stat);
    if (s_pinned && error == __WASI_ERRNO_SUCCESS)
        pin_stat(stat);
    return error;
}

__wasi_errno_t __wrap___wasi_fd_filestat_get(__wasi_fd_t fd, __wasi_filestat_t *stat) {
    __wasi_errno_t error = __real___wasi_fd_filestat_get(fd, stat);
    if (s_pinned && error == __WASI_ERRNO_SUCCESS)
        pin_stat(stat);
    return error;
}

static void fail_status(PyStatus status) {
    if (PyStatus_Exception(status))
        Py_ExitStatusException(status);
}

__attribute__((export_name("wizer-initialize"))) void pynp_wizer_initialize(void) {
    construct();

    /* The interpreter a task gets: the sandbox's command line with an empty program, the
     * sandbox's environment as wizer passes it (PYTHONHOME, PYTHONPATH, PYTHONHASHSEED...). */
    wchar_t *argv[] = {L"python.wasm", L"-P", L"-B", L"-c", L"", NULL};
    PyConfig config;
    PyConfig_InitPythonConfig(&config);
    PyStatus status = PyConfig_SetArgv(&config, 5, argv);
    if (!PyStatus_Exception(status))
        status = Py_InitializeFromConfig(&config);
    PyConfig_Clear(&config);
    fail_status(status);

    /* Imported, not bound: the task's __main__ stays as a fresh interpreter leaves it. */
    static const char *preimport[] = {"numpy", "numpy.linalg", "numpy.fft", "numpy.random", NULL};
    for (const char **name = preimport; *name != NULL; name++) {
        PyObject *module = PyImport_ImportModule(*name);
        if (module == NULL) {
            PyErr_Print();
            abort();
        }
        Py_DECREF(module);
    }
    PyObject *gc = PyImport_ImportModule("gc");
    PyObject *frozen = gc == NULL ? NULL : PyObject_CallMethod(gc, "collect", NULL);
    Py_XDECREF(frozen);
    frozen = gc == NULL ? NULL : PyObject_CallMethod(gc, "freeze", NULL);
    if (frozen == NULL) {
        PyErr_Print();
        abort();
    }
    Py_DECREF(frozen);
    Py_DECREF(gc);

    s_initialized = 1;
    s_pinned = 0;
}

static PyObject *task_argv(char **argv, size_t argc) {
    PyObject *list = PyList_New(0);
    PyObject *item = list == NULL ? NULL : PyUnicode_FromString("-c");
    if (item == NULL || PyList_Append(list, item) < 0)
        return NULL;
    Py_DECREF(item);
    for (size_t i = 5; i < argc; i++) {
        item = PyUnicode_DecodeFSDefault(argv[i]);
        if (item == NULL || PyList_Append(list, item) < 0)
            return NULL;
        Py_DECREF(item);
    }
    return list;
}

__attribute__((export_name("_start"))) void pynp_start(void) {
    construct();
    s_pinned = 0;

    __wasi_size_t argc = 0, size = 0;
    if (__wasi_args_sizes_get(&argc, &size) != __WASI_ERRNO_SUCCESS)
        abort();
    char **argv = calloc(argc + 1, sizeof(char *));
    char *buffer = malloc(size > 0 ? size : 1);
    if (argv == NULL || buffer == NULL || __wasi_args_get((uint8_t **)argv, (uint8_t *)buffer) != __WASI_ERRNO_SUCCESS)
        abort();

    if (!s_initialized)
        exit(Py_BytesMain((int)argc, argv));

    /* The snapshot runs what the sandbox runs: -P -B -c <code> [args], as CPython's -c does. */
    if (argc < 5 || strcmp(argv[1], "-P") != 0 || strcmp(argv[2], "-B") != 0 || strcmp(argv[3], "-c") != 0) {
        static const char usage[] = "this python.wasm is a snapshot: it runs only -P -B -c <code> [args]\n";
        (void)write(2, usage, sizeof usage - 1);
        exit(2);
    }
    PyObject *args = task_argv(argv, argc);
    if (args == NULL || PySys_SetObject("argv", args) < 0) {
        PyErr_Print();
        exit(1);
    }
    Py_DECREF(args);
    PyCompilerFlags flags = _PyCompilerFlags_INIT;
    flags.cf_flags |= PyCF_IGNORE_COOKIE;
    int code = PyRun_SimpleStringFlags(argv[4], &flags) != 0 ? 1 : 0;
    if (Py_FinalizeEx() < 0)
        code = 120;
    exit(code);
}
