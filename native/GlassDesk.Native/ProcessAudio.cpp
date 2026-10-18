#include "ProcessAudio.h"
#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <mmdeviceapi.h>
#include <wrl.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <cmath>
#include <mutex>
#include <new>
#include <thread>

using Microsoft::WRL::ComPtr;

namespace
{
    class Handle
    {
        HANDLE value_{};
    public:
        explicit Handle(HANDLE value = nullptr) noexcept : value_(value) {}
        ~Handle() { if (value_ && value_ != INVALID_HANDLE_VALUE) CloseHandle(value_); }
        Handle(Handle const&) = delete;
        Handle& operator=(Handle const&) = delete;
        HANDLE get() const noexcept { return value_; }
    };

    // Activation can outlive a timed-out caller. The OS holds this self-contained
    // COM object; its callback never refers to the capture or a caller buffer.
    class Activation final : public Microsoft::WRL::RuntimeClass<
        Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
        IActivateAudioInterfaceCompletionHandler, Microsoft::WRL::FtmBase>
    {
    public:
        Handle completed{CreateEventW(nullptr, FALSE, FALSE, nullptr)};
        HRESULT result{E_PENDING};
        ComPtr<IAudioClient> client;
        HRESULT STDMETHODCALLTYPE ActivateCompleted(IActivateAudioInterfaceAsyncOperation* operation) override
        {
            ComPtr<IUnknown> object;
            HRESULT activationResult = E_FAIL;
            result = operation->GetActivateResult(&activationResult, &object);
            if (SUCCEEDED(result)) result = activationResult;
            if (SUCCEEDED(result)) result = object.As(&client);
            SetEvent(completed.get());
            return S_OK;
        }
    };

    constexpr UINT32 SampleRate = 48000;
    constexpr UINT32 Channels = 2;
    constexpr UINT32 RingFrames = 8192;
    class Capture
    {
        Handle process_;
        Handle stop_{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
        Handle ready_{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
        std::thread worker_;
        std::atomic<HRESULT> result_{E_PENDING};
        std::mutex mutex_;
        std::array<float, RingFrames * Channels> ring_{};
        UINT32 writeFrame_{};
        UINT32 validFrames_{};
        bool unread_{};
        std::int64_t timestamp_{};
        DWORD processId_{};

        HRESULT Initialize(ComPtr<IAudioClient>& client, ComPtr<IAudioCaptureClient>& reader, HANDLE sampleEvent)
        {
            auto callback = Microsoft::WRL::Make<Activation>();
            if (!callback || !callback->completed.get()) return E_OUTOFMEMORY;
            AUDIOCLIENT_ACTIVATION_PARAMS parameters{};
            parameters.ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
            parameters.ProcessLoopbackParams.TargetProcessId = processId_;
            parameters.ProcessLoopbackParams.ProcessLoopbackMode = PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;
            PROPVARIANT blob{};
            blob.vt = VT_BLOB;
            blob.blob.cbSize = sizeof(parameters);
            blob.blob.pBlobData = reinterpret_cast<BYTE*>(&parameters);
            ComPtr<IActivateAudioInterfaceAsyncOperation> operation;
            HRESULT hr = ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
                __uuidof(IAudioClient), &blob, callback.Get(), &operation);
            if (FAILED(hr)) return hr;
            HANDLE events[]{stop_.get(), callback->completed.get()};
            DWORD wait = WaitForMultipleObjects(2, events, FALSE, 5000);
            if (wait == WAIT_OBJECT_0) return HRESULT_FROM_WIN32(ERROR_CANCELLED);
            if (wait == WAIT_TIMEOUT) return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
            if (wait != WAIT_OBJECT_0 + 1) return HRESULT_FROM_WIN32(GetLastError());
            if (FAILED(callback->result)) return callback->result;
            client = callback->client;
            WAVEFORMATEX format{};
            format.wFormatTag = WAVE_FORMAT_IEEE_FLOAT;
            format.nChannels = Channels;
            format.nSamplesPerSec = SampleRate;
            format.wBitsPerSample = 32;
            format.nBlockAlign = Channels * sizeof(float);
            format.nAvgBytesPerSec = SampleRate * format.nBlockAlign;
            hr = client->Initialize(AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM,
                0, 0, &format, nullptr);
            if (FAILED(hr)) return hr;
            hr = client->SetEventHandle(sampleEvent);
            if (FAILED(hr)) return hr;
            return client->GetService(IID_PPV_ARGS(&reader));
        }

        HRESULT Drain(IAudioCaptureClient* reader)
        {
            UINT32 pending = 0;
            HRESULT hr = reader->GetNextPacketSize(&pending);
            while (SUCCEEDED(hr) && pending)
            {
                BYTE* data = nullptr;
                UINT32 frames = 0;
                DWORD flags = 0;
                hr = reader->GetBuffer(&data, &frames, &flags, nullptr, nullptr);
                if (FAILED(hr)) return hr;
                {
                    std::lock_guard lock(mutex_);
                    if (flags & AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) validFrames_ = 0;
                    auto samples = reinterpret_cast<float const*>(data);
                    for (UINT32 frame = 0; frame < frames; ++frame)
                    {
                        for (UINT32 channel = 0; channel < Channels; ++channel)
                        {
                            float sample = (flags & AUDCLNT_BUFFERFLAGS_SILENT) || !samples ? 0.f : samples[frame * Channels + channel];
                            ring_[writeFrame_ * Channels + channel] = std::isfinite(sample) ? std::clamp(sample, -1.f, 1.f) : 0.f;
                        }
                        writeFrame_ = (writeFrame_ + 1) % RingFrames;
                        validFrames_ = std::min(validFrames_ + 1, RingFrames);
                    }
                    LARGE_INTEGER now{};
                    QueryPerformanceCounter(&now);
                    timestamp_ = now.QuadPart;
                    unread_ = frames != 0;
                }
                hr = reader->ReleaseBuffer(frames);
                if (FAILED(hr)) return hr;
                hr = reader->GetNextPacketSize(&pending);
            }
            return hr;
        }

        void Run() noexcept
        {
            HRESULT apartment = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
            if (FAILED(apartment)) { result_ = apartment; SetEvent(ready_.get()); return; }
            {
                ComPtr<IAudioClient> client;
                ComPtr<IAudioCaptureClient> reader;
                Handle sampleEvent{CreateEventW(nullptr, FALSE, FALSE, nullptr)};
                HRESULT hr = sampleEvent.get() ? Initialize(client, reader, sampleEvent.get()) : HRESULT_FROM_WIN32(GetLastError());
                if (SUCCEEDED(hr)) hr = client->Start();
                result_ = hr;
                SetEvent(ready_.get());
                if (SUCCEEDED(hr))
                {
                    HANDLE events[]{stop_.get(), sampleEvent.get(), process_.get()};
                    for (;;)
                    {
                        DWORD wait = WaitForMultipleObjects(3, events, FALSE, 100);
                        if (wait == WAIT_OBJECT_0) break;
                        if (wait == WAIT_OBJECT_0 + 2) { hr = HRESULT_FROM_WIN32(ERROR_PROCESS_ABORTED); break; }
                        if (wait == WAIT_FAILED) { hr = HRESULT_FROM_WIN32(GetLastError()); break; }
                        // A bounded poll also drains buffers when a driver omits its event.
                        hr = Drain(reader.Get());
                        if (FAILED(hr)) break;
                    }
                    client->Stop();
                    result_ = hr;
                }
            }
            CoUninitialize();
        }

    public:
        explicit Capture(HANDLE process, DWORD processId) : process_(process), processId_(processId) {}
        ~Capture() { SetEvent(stop_.get()); if (worker_.joinable()) worker_.join(); }
        HRESULT Start()
        {
            if (!stop_.get() || !ready_.get()) return HRESULT_FROM_WIN32(GetLastError());
            worker_ = std::thread([this] { Run(); });
            DWORD wait = WaitForSingleObject(ready_.get(), 6000);
            return wait == WAIT_OBJECT_0 ? result_.load() : HRESULT_FROM_WIN32(wait == WAIT_TIMEOUT ? ERROR_TIMEOUT : GetLastError());
        }
        HRESULT Read(float* samples, UINT32 capacity, UINT32* frames, UINT32* rate, std::int64_t* timestamp)
        {
            *frames = 0; *rate = SampleRate; *timestamp = 0;
            HRESULT hr = result_.load();
            if (FAILED(hr)) return hr;
            std::lock_guard lock(mutex_);
            if (!unread_) return S_FALSE;
            UINT32 count = std::min(validFrames_, capacity);
            UINT32 first = (writeFrame_ + RingFrames - count) % RingFrames;
            for (UINT32 index = 0; index < count; ++index)
                for (UINT32 channel = 0; channel < Channels; ++channel)
                    samples[index * Channels + channel] = ring_[((first + index) % RingFrames) * Channels + channel];
            *frames = count; *timestamp = timestamp_; unread_ = false;
            return S_OK;
        }
    };
}

HRESULT __stdcall GD_AudioCreate(DWORD processId, std::uint64_t processCreationTime, void** capture)
{
    if (!capture || !processId || !processCreationTime) return E_INVALIDARG;
    *capture = nullptr;
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, FALSE, processId);
    if (!process) return HRESULT_FROM_WIN32(GetLastError());
    FILETIME creation{}, exit{}, kernel{}, user{};
    if (!GetProcessTimes(process, &creation, &exit, &kernel, &user))
    { HRESULT hr = HRESULT_FROM_WIN32(GetLastError()); CloseHandle(process); return hr; }
    std::uint64_t actual = (static_cast<std::uint64_t>(creation.dwHighDateTime) << 32) | creation.dwLowDateTime;
    if (actual != processCreationTime || WaitForSingleObject(process, 0) != WAIT_TIMEOUT)
    { CloseHandle(process); return HRESULT_FROM_WIN32(ERROR_PROCESS_ABORTED); }
    Capture* instance = nullptr;
    try
    {
        instance = new Capture(process, processId);
        process = nullptr;
        HRESULT hr = instance->Start();
        if (FAILED(hr)) { delete instance; return hr; }
        *capture = instance;
        return S_OK;
    }
    catch (std::bad_alloc const&) { if (process) CloseHandle(process); delete instance; return E_OUTOFMEMORY; }
    catch (...) { if (process) CloseHandle(process); delete instance; return E_FAIL; }
}

HRESULT __stdcall GD_AudioRead(void* capture, float* stereoSamples, UINT32 capacityFrames,
    UINT32* frames, UINT32* sampleRate, std::int64_t* qpcTimestamp)
{
    if (!capture || !stereoSamples || !capacityFrames || !frames || !sampleRate || !qpcTimestamp) return E_INVALIDARG;
    return static_cast<Capture*>(capture)->Read(stereoSamples, capacityFrames, frames, sampleRate, qpcTimestamp);
}

void __stdcall GD_AudioDestroy(void* capture) { delete static_cast<Capture*>(capture); }
