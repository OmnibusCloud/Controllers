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
 * The sandbox needs no change: it calls _start, never _initialize.
 *
 * What a task sees is what a fresh interpreter would show it:
 * - sys.argv and sys.orig_argv are the task's, and the code runs as CPython's -c runs it
 *   (decoded, dedented, compiled as "<string>" with its source kept for tracebacks);
 * - what drew entropy while the snapshot was made is seeded again from the task's stream when
 *   the task starts (Python's random, numpy's legacy global RandomState, tempfile's name
 *   generator), so unseeded random numbers follow the task's seed;
 * - the clocks read the sandbox's pinned start (0) while the snapshot is made, so no value of
 *   the build's time is in it;
 * - the environment and the preopened "/" are checked against the ones the snapshot was made
 *   with, and a run under others stops with exit code 2 and a line saying so: the snapshot holds
 *   os.environ, sys.path and zipimport's view of the layers.
 * numpy is in sys.modules already (not in the task's namespace).
 *
 * The same bytes from every build: while s_pinned is set (from the first constructor to the end
 * of wizer-initialize, and in a plain run until _start begins) the clocks answer 0, entropy
 * answers zeros, and file metadata keeps size and type with no device, inode or times.
 * snapshot.sh links with --wrap for the four calls. The approach is wasi-python's (Apache-2.0,
 * github.com/ClayGendron/wasi_python, recipes/reactor_main.c). */
#include <Python.h>

#include "pycore_pylifecycle.h"   /* _PyRun_SimpleStringFlagsWithName(), as Modules/main.c */
#include "pycore_unicodeobject.h" /* _PyUnicode_Dedent() */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#include <wasi/api.h>

#ifndef PYNP_SOURCE_DATE_EPOCH
#error "PYNP_SOURCE_DATE_EPOCH must be defined (snapshot.sh)"
#endif

#define PYNP_ROOT_FD 3
#define PYNP_REFUSED_EXIT 2

extern void __wasm_call_ctors(void);

static int s_constructed;
static int s_initialized;
static volatile int s_pinned = 1;
static unsigned long s_entropy_calls;
static unsigned long s_entropy_bytes;
static char **s_environment;
static size_t s_environment_count;

/* Run at the start of every task in the snapshot: what drew entropy while the snapshot was
 * made draws again from the task's stream (random_get, seeded by the task). */
static const char RESEED[] =
    "import sys\n"
    "_random = sys.modules.get('random')\n"
    "if _random is not None:\n"
    "    _random.seed()\n"
    "_tempfile = sys.modules.get('tempfile')\n"
    "if _tempfile is not None:\n"
    "    _tempfile._name_sequence = None\n"
    "_mtrand = sys.modules.get('numpy.random.mtrand')\n"
    "if _mtrand is not None:\n"
    "    _mtrand._rand.seed()\n";

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
    /* The sandbox's clock starts at 0 for every clock id (VerifyDeterministicLinker). */
    (void)id;
    (void)precision;
    *time = 0;
    return __WASI_ERRNO_SUCCESS;
}

__wasi_errno_t __wrap___wasi_random_get(uint8_t *buffer, __wasi_size_t length) {
    if (!s_pinned)
        return __real___wasi_random_get(buffer, length);
    s_entropy_calls++;
    s_entropy_bytes += length;
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

static int compare_strings(const void *left, const void *right) {
    return strcmp(*(char *const *)left, *(char *const *)right);
}

/* The host's environment as sorted "NAME=value" strings; the caller frees both blocks. */
static char **read_environment(size_t *count, char **block) {
    __wasi_size_t entries = 0, size = 0;
    if (__wasi_environ_sizes_get(&entries, &size) != __WASI_ERRNO_SUCCESS)
        abort();
    char **list = calloc(entries + 1, sizeof(char *));
    *block = malloc(size > 0 ? size : 1);
    if (list == NULL || *block == NULL || __wasi_environ_get((uint8_t **)list, (uint8_t *)*block) != __WASI_ERRNO_SUCCESS)
        abort();
    qsort(list, entries, sizeof(char *), compare_strings);
    *count = entries;
    return list;
}

/* The guest's file system must be the one the snapshot was made with: "/" at fd 3, alone. */
static int preopens_match(void) {
    __wasi_prestat_t prestat;
    char name[2];
    if (__wasi_fd_prestat_get(PYNP_ROOT_FD, &prestat) != __WASI_ERRNO_SUCCESS
        || prestat.tag != __WASI_PREOPENTYPE_DIR || prestat.u.dir.pr_name_len != 1
        || __wasi_fd_prestat_dir_name(PYNP_ROOT_FD, (uint8_t *)name, 1) != __WASI_ERRNO_SUCCESS || name[0] != '/')
        return 0;
    return __wasi_fd_prestat_get(PYNP_ROOT_FD + 1, &prestat) != __WASI_ERRNO_SUCCESS;
}

static int environment_matches(void) {
    size_t count;
    char *block;
    char **current = read_environment(&count, &block);
    int same = count == s_environment_count;
    for (size_t i = 0; same && i < count; i++)
        same = strcmp(current[i], s_environment[i]) == 0;
    free(current);
    free(block);
    return same;
}

static void refuse(const char *message) {
    (void)write(2, message, strlen(message));
    exit(PYNP_REFUSED_EXIT);
}

static void fail_status(PyStatus status) {
    if (PyStatus_Exception(status))
        Py_ExitStatusException(status);
}

__attribute__((export_name("wizer-initialize"))) void pynp_wizer_initialize(void) {
    construct();
    unsigned long constructor_calls = s_entropy_calls, constructor_bytes = s_entropy_bytes;

    /* The environment the snapshot holds, kept to compare with every run's. */
    char *block;
    s_environment = read_environment(&s_environment_count, &block);

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
    unsigned long init_calls = s_entropy_calls - constructor_calls, init_bytes = s_entropy_bytes - constructor_bytes;

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
    PyObject *result = gc == NULL ? NULL : PyObject_CallMethod(gc, "collect", NULL);
    Py_XDECREF(result);
    result = gc == NULL ? NULL : PyObject_CallMethod(gc, "freeze", NULL);
    if (result == NULL) {
        PyErr_Print();
        abort();
    }
    Py_DECREF(result);
    Py_DECREF(gc);

    /* The build log says what drew entropy, and which modules with state an import may seed
     * the snapshot holds (RESEED seeds each again). */
    fprintf(stderr, "pynp: entropy while the snapshot was made: constructors %lu calls (%lu bytes), "
                    "interpreter start %lu calls (%lu bytes), imports %lu calls (%lu bytes)\n",
            constructor_calls, constructor_bytes, init_calls, init_bytes,
            s_entropy_calls - constructor_calls - init_calls, s_entropy_bytes - constructor_bytes - init_bytes);
    if (PyRun_SimpleString(
            "import sys\n"
            "print('pynp: modules in the snapshot:', len(sys.modules), file=sys.stderr)\n"
            "print('pynp: of those with state an import may seed:', sorted(m for m in ('random', 'tempfile', 'uuid', "
            "'secrets', 'hashlib', 'numpy.random.mtrand') if m in sys.modules), file=sys.stderr)\n") != 0)
        abort();

    s_initialized = 1;
    s_pinned = 0;
}

static PyObject *string_list(char **items, size_t from, size_t to, const char *first) {
    PyObject *list = PyList_New(0);
    if (list == NULL)
        return NULL;
    if (first != NULL) {
        PyObject *item = PyUnicode_FromString(first);
        if (item == NULL || PyList_Append(list, item) < 0)
            return NULL;
        Py_DECREF(item);
    }
    for (size_t i = from; i < to; i++) {
        PyObject *item = PyUnicode_DecodeFSDefault(items[i]);
        if (item == NULL || PyList_Append(list, item) < 0)
            return NULL;
        Py_DECREF(item);
    }
    return list;
}

static void set_sys_list(const char *name, PyObject *list) {
    if (list == NULL || PySys_SetObject(name, list) < 0) {
        PyErr_Print();
        exit(1);
    }
    Py_DECREF(list);
}

/* CPython's pymain_run_command (Modules/main.c), the same steps: the command decoded as argv is,
 * audited, dedented, and run under the name "<string>" so a traceback shows its lines. */
static int run_command(const char *command) {
    PyObject *unicode = PyUnicode_DecodeFSDefault(command);
    if (unicode == NULL)
        goto error;
    if (PySys_Audit("cpython.run_command", "O", unicode) < 0) {
        Py_DECREF(unicode);
        PyErr_Print();
        return 1;
    }
    Py_SETREF(unicode, _PyUnicode_Dedent(unicode));
    if (unicode == NULL)
        goto error;
    PyObject *bytes = PyUnicode_AsUTF8String(unicode);
    Py_DECREF(unicode);
    if (bytes == NULL)
        goto error;
    PyCompilerFlags flags = _PyCompilerFlags_INIT;
    flags.cf_flags |= PyCF_IGNORE_COOKIE;
    int failed = _PyRun_SimpleStringFlagsWithName(PyBytes_AsString(bytes), "<string>", &flags);
    Py_DECREF(bytes);
    return failed != 0;

error:
    PySys_WriteStderr("Unable to decode the command from the command line:
");
    PyErr_Print();
    return 1;
}

static void reseed(void) {
    PyObject *globals = PyDict_New();
    PyObject *builtins = PyImport_ImportModule("builtins");
    if (globals == NULL || builtins == NULL || PyDict_SetItemString(globals, "__builtins__", builtins) < 0) {
        PyErr_Print();
        exit(1);
    }
    Py_DECREF(builtins);
    PyObject *done = PyRun_String(RESEED, Py_file_input, globals, globals);
    if (done == NULL) {
        PyErr_Print();
        exit(1);
    }
    Py_DECREF(done);
    Py_DECREF(globals);
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

    /* The snapshot runs what the sandbox runs: -P -B -c <code> [args], as CPython's -c does,
     * under the environment and the file system it was made with. */
    if (argc < 5 || strcmp(argv[1], "-P") != 0 || strcmp(argv[2], "-B") != 0 || strcmp(argv[3], "-c") != 0)
        refuse("this python.wasm is a snapshot: it runs only -P -B -c <code> [args]\n");
    if (!environment_matches())
        refuse("this python.wasm is a snapshot: the environment differs from the one it was made with\n");
    if (!preopens_match())
        refuse("this python.wasm is a snapshot: its file system must be \"/\" alone, as when it was made\n");

    set_sys_list("argv", string_list(argv, 5, argc, "-c"));
    set_sys_list("orig_argv", string_list(argv, 0, argc, NULL));
    reseed();

    int code = run_command(argv[4]);
    if (Py_FinalizeEx() < 0)
        code = 120;
    exit(code);
}
