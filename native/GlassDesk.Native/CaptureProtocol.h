#pragma once
#include <windows.h>
#include <cstdint>

namespace gd
{
// Anonymous inherited handles, restricted to this host's job-owned child.
// Only geometry/counters cross CPU memory. Desktop pixels remain on the GPU.
struct CaptureControl
{
    uint32_t magic{0x47444332};
    LUID adapter{};
    uint64_t monitor{},textureHandle{};
    unsigned width{},height{};
    volatile LONG request{},delivered{},frameRequest{},error{E_PENDING},callbacks{},received{};
    RECT source{};
};
}
