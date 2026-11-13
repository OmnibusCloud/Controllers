/* numpy's own floating-point signals, kept in software (adapted from wasi-python, Apache-2.0,
 * github.com/ClayGendron/wasi_python @25e49df5, recipes/npy_fenv_flags.h: the flag word renamed).
 * Force-included into every numpy C and C++ compile by wasi-numpy.cross.in.
 *
 * WebAssembly has no floating-point status flags, and wasi-libc's <fenv.h> defines no
 * FE_INVALID, FE_DIVBYZERO, FE_OVERFLOW or FE_UNDERFLOW (FE_ALL_EXCEPT is 0). numpy then defines
 * the four as 0 itself, so the signals it raises in software are lost:
 * npy_set_floatstatus_invalid() becomes feraiseexcept(0). That call is how umath_linalg reports
 * a failed LAPACK call, so numpy.linalg.inv of a singular matrix would return NaNs instead of
 * raising LinAlgError, and an integer division by zero would warn nowhere.
 *
 * Here the flags get bits, and numpy's three calls (feraiseexcept, fetestexcept, feclearexcept)
 * set, test and clear one flag word: a weak definition in every object, of which the static
 * link keeps one. The guest has one thread. Arithmetic itself still sets nothing: wasm has no
 * flag for 1.0 / 0.0 to set, so np.float64(1) / 0 gives inf without a warning.
 *
 * The #error below stops the build the day wasi-libc defines the flags itself; the header then
 * goes. */
#ifndef PYNP_NPY_FENV_FLAGS_H
#define PYNP_NPY_FENV_FLAGS_H

#include <fenv.h>
#ifdef __cplusplus
#include <cfenv>
#endif

#if defined(FE_INVALID) || defined(FE_DIVBYZERO) || defined(FE_OVERFLOW) || defined(FE_UNDERFLOW)
#error "the C library now defines floating-point exception flags: remove npy_fenv_flags.h"
#endif

#define FE_INVALID 0x01
#define FE_DIVBYZERO 0x04
#define FE_OVERFLOW 0x08
#define FE_UNDERFLOW 0x10
#define FE_INEXACT 0x20
#undef FE_ALL_EXCEPT
#define FE_ALL_EXCEPT (FE_INVALID | FE_DIVBYZERO | FE_OVERFLOW | FE_UNDERFLOW | FE_INEXACT)

#ifdef __cplusplus
extern "C" {
#endif
__attribute__((weak)) int pynp_fe_flags;

static inline int pynp_feraiseexcept(int excepts) {
    pynp_fe_flags |= excepts & FE_ALL_EXCEPT;
    return 0;
}

static inline int pynp_fetestexcept(int excepts) {
    return pynp_fe_flags & excepts & FE_ALL_EXCEPT;
}

static inline int pynp_feclearexcept(int excepts) {
    pynp_fe_flags &= ~(excepts & FE_ALL_EXCEPT);
    return 0;
}
#ifdef __cplusplus
}
/* numpy calls the three unqualified; the std:: spelling compiles too. */
namespace std {
using ::pynp_feclearexcept;
using ::pynp_feraiseexcept;
using ::pynp_fetestexcept;
}
#endif

#define feraiseexcept pynp_feraiseexcept
#define fetestexcept pynp_fetestexcept
#define feclearexcept pynp_feclearexcept

#endif
