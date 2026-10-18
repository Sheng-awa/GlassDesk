#pragma once
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <cstdint>

// In-memory, stereo IEEE float process-tree loopback. No endpoint/microphone fallback.
extern "C" __declspec(dllexport) HRESULT __stdcall GD_AudioCreate(
    DWORD processId, std::uint64_t processCreationTime, void** capture);
extern "C" __declspec(dllexport) HRESULT __stdcall GD_AudioRead(
    void* capture, float* stereoSamples, UINT32 capacityFrames, UINT32* frames,
    UINT32* sampleRate, std::int64_t* qpcTimestamp);
extern "C" __declspec(dllexport) void __stdcall GD_AudioDestroy(void* capture);
