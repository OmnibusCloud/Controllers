/* numpy's extension modules as builtins of python.wasm.
 *
 * wasi-libc has no dlopen, so each numpy extension module is linked into the interpreter and
 * named in the builtin table under its full dotted name: CPython's BuiltinImporter compares the
 * whole name (Python/import.c, is_builtin), so `from . import _multiarray_umath` inside
 * numpy._core finds "numpy._core._multiarray_umath" there. The table has to be complete before
 * Py_Initialize, and a constructor runs before main (from __wasm_call_ctors), so CPython's own
 * Programs/python.c stays as it is. The guard keeps a second call harmless.
 *
 * The entries come from npyinittab.h, which build.sh generates from the module archives numpy's
 * build produced (numpy_builtins.py). The module _wasinumpy itself exists because a Setup.local
 * line needs a module name; it tells a program which numpy modules are built in. */
#include <Python.h>

#include "npyinittab.h"

static int s_registered;

__attribute__((constructor)) static void register_numpy_builtins(void) {
    if (s_registered)
        return;
    s_registered = 1;
    for (size_t i = 0; i < sizeof PYNP_INITTAB / sizeof PYNP_INITTAB[0]; i++) {
        if (PyImport_AppendInittab(PYNP_INITTAB[i].name, PYNP_INITTAB[i].initfunc) != 0)
            abort();
    }
}

static int wasinumpy_exec(PyObject *module) {
    PyObject *names = PyTuple_New((Py_ssize_t)(sizeof PYNP_INITTAB / sizeof PYNP_INITTAB[0]));
    if (names == NULL)
        return -1;
    for (size_t i = 0; i < sizeof PYNP_INITTAB / sizeof PYNP_INITTAB[0]; i++) {
        PyObject *name = PyUnicode_FromString(PYNP_INITTAB[i].name);
        if (name == NULL) {
            Py_DECREF(names);
            return -1;
        }
        PyTuple_SET_ITEM(names, (Py_ssize_t)i, name);
    }
    if (PyModule_Add(module, "modules", names) < 0)
        return -1;
    return PyModule_AddStringConstant(module, "numpy_version", PYNP_NUMPY_VERSION);
}

static PyModuleDef_Slot wasinumpy_slots[] = {
    {Py_mod_exec, wasinumpy_exec},
    {Py_mod_multiple_interpreters, Py_MOD_PER_INTERPRETER_GIL_SUPPORTED},
    {0, NULL},
};

static struct PyModuleDef wasinumpy_module = {
    PyModuleDef_HEAD_INIT,
    .m_name = "_wasinumpy",
    .m_doc = "The numpy extension modules this interpreter has built in.",
    .m_size = 0,
    .m_slots = wasinumpy_slots,
};

PyMODINIT_FUNC PyInit__wasinumpy(void) {
    return PyModuleDef_Init(&wasinumpy_module);
}
