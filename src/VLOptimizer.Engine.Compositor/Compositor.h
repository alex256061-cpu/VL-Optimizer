// VL compositor — window capture with GPU scaling, no external scaling program required.
//
// Model: the compositor owns its own window and D3D11 device. Each frame it captures a target
// window through DXGI Desktop Duplication, copies the frame into a scaled render target, and presents
// it on its own swapchain. The captured program keeps rendering normally to its own window; the
// compositor never injects, hooks, or patches it.
//
// Limitations of Desktop Duplication that shape this design:
//   - It captures the OUTPUT (monitor), not a window. A window is therefore cropped out of the
//     desktop frame using its client rectangle.
//   - It requires the process to run on the same desktop session and fails while a protected
//     content path (HDCP) is active.
//   - Frames arrive only when the desktop changes; a static window yields timeout, not a stall.

#pragma once

#include <windows.h>
#include <d3d11.h>
#include <dxgi1_6.h>
#include <wrl/client.h>

#include <cstdint>
#include <string>

using Microsoft::WRL::ComPtr;

namespace vl {

enum class ScaleMode {
    Nearest = 0,  // fastest, blocky
    Bilinear,     // cheap and smooth, slightly soft
    Bicubic,      // sharper than bilinear, a little slower
};

enum class FitMode {
    Stretch = 0,  // fill the output, may distort
    Fit,          // preserve aspect, letterbox
};

struct CompositorOptions {
    std::wstring TargetExecutableOrTitle;
    uint32_t OutputWidth = 0;   // 0 = keep the captured size
    uint32_t OutputHeight = 0;
    ScaleMode Scale = ScaleMode::Bicubic;
    FitMode Fit = FitMode::Fit;
    bool DrawFps = true;
    bool ShowCursor = false;
    int TargetFps = 0;          // 0 = uncapped
};

// A resolved capture target: the window handle plus the identity used to detect it going away.
struct CaptureTarget {
    HWND Window = nullptr;
    uint32_t ProcessId = 0;
    std::wstring ProcessPath;
    uint32_t Width = 0;
    uint32_t Height = 0;
};

// Finds a top-level, visible window belonging to a process whose executable name or window title
// matches. Returns the largest such window, which skips splash screens and tool windows.
bool ResolveCaptureTarget(const std::wstring& executableOrTitle, CaptureTarget* out, std::wstring* error);

// Owns the output device, the duplication reader, and the scaling pipeline.
class Compositor {
public:
    Compositor();
    ~Compositor();

    Compositor(const Compositor&) = delete;
    Compositor& operator=(const Compositor&) = delete;

    // Creates the output window, device, swapchain and duplication for the current adapter output.
    bool Start(const CompositorOptions& options, std::wstring* error);

    // Captures and presents one frame. Returns false when there is nothing new (a normal condition,
    // not an error) so the caller can pace itself.
    bool RenderFrame(std::wstring* error);

    void Stop();

    HWND OutputWindow() const { return window_; }
    int64_t PresentedFrames() const { return presented_; }
    double CurrentFps() const { return fps_; }
    const CaptureTarget& Target() const { return target_; }
    bool TargetLost() const { return targetLost_; }

    // Re-resolves the target after the window closed or the process restarted.
    bool RebindTarget(std::wstring* error);

private:
    bool CreateOutputWindow(std::wstring* error);
    bool CreateDevice(std::wstring* error);
    bool CreateSwapchain(std::wstring* error);
    bool CreateDuplication(std::wstring* error);
    bool CreateScalePipeline(std::wstring* error);
    void MeasureFps();
    void DestroyDuplication();

    CompositorOptions options_;
    CaptureTarget target_;
    bool targetLost_ = false;

    HWND window_ = nullptr;
    ComPtr<ID3D11Device> device_;
    ComPtr<ID3D11DeviceContext> context_;
    ComPtr<IDXGISwapChain1> swapchain_;

    // Output side
    ComPtr<ID3D11Texture2D> scaledTexture_;
    ComPtr<ID3D11RenderTargetView> scaledTarget_;
    uint32_t outputWidth_ = 0;
    uint32_t outputHeight_ = 0;

    // Duplication side
    ComPtr<IDXGIOutputDuplication> duplication_;
    ComPtr<ID3D11Texture2D> staging_;  // CPU-readable copy of the captured frame
    uint32_t captureWidth_ = 0;
    uint32_t captureHeight_ = 0;
    IDXGIAdapter1* duplicationAdapter_ = nullptr;
    IDXGIOutput* duplicationOutput_ = nullptr;

    // Scaling pass
    ComPtr<ID3D11VertexShader> scaleVertexShader_;
    ComPtr<ID3D11PixelShader> scalePixelShader_;
    ComPtr<ID3D11SamplerState> scaleSampler_;
    ComPtr<ID3D11SamplerState> pointSampler_;
    ComPtr<ID3D11Buffer> scaleConstants_;
    ComPtr<ID3D11Texture2D> sourceTexture_;
    ComPtr<ID3D11ShaderResourceView> sourceView_;
    ComPtr<ID3D11Texture2D> captureCopy_;  // GPU copy of the cropped region, source of the scale pass
    ComPtr<ID3D11ShaderResourceView> captureCopyView_;
    uint32_t sourceWidth_ = 0;
    uint32_t sourceHeight_ = 0;
    bool pipelineDirty_ = true;

    // FPS accounting
    int64_t presented_ = 0;
    int64_t fpsWindowStart_ = 0;
    int64_t fpsWindowFrames_ = 0;
    double fps_ = 0.0;
};

}  // namespace vl
