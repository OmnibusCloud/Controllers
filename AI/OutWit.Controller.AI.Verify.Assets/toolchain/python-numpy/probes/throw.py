# The one way this build cannot recover: a C++ throw stops the guest (cxa_stubs.c). numpy checks
# its arguments before its C++ code would throw, so this is a probe that the check holds.
import numpy as np

for n in (0, -1):
    try:
        np.fft.fft(np.ones(4), n=n)
        print(f"fft n={n}: no error")
    except ValueError as error:
        print(f"fft n={n}: ValueError: {error}")
print(np.fft.fft(np.ones(7)).round(6).tolist())
