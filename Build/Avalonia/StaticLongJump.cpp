// The Zig/MinGW graphics libraries call longjmp through an import pointer. Resolve that pointer to
// MSVC's statically linked implementation, rather than pulling in VCRUNTIME140.dll just for longjmp.
// Windows x64 passes jmp_buf as a pointer; no CRT header or structure layout is needed for this alias.
extern "C" __declspec(noreturn) void __cdecl longjmp(void* buffer, int value);
extern "C" void (__cdecl* const __imp_longjmp)(void*, int) = &longjmp;
