using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace MiniMonitor.Video;

/// <summary>
/// 基于 WebView2 / Chromium 的硬件加速视频预览控件。
/// 支持视频、音频（黑屏播放声音）与图片，暴露播放、暂停、定位、逐帧、音量、速度与 CSS 滤镜。
/// </summary>
internal sealed class VideoPreviewControl : UserControl
{
    private const string MediaHost = "minimonitor.example";
    private readonly WebView2 _web;
    private readonly Label _message;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MediaPreviewServer? _server;
    private Task? _initTask;
    private TaskCompletionSource<bool>? _readySource;
    private bool _ready;
    private bool _disposed;
    private double _duration;
    private double _position;
    private bool _playing;
    private double _volume = 0.8;

    public event EventHandler<double>? PositionChanged;
    public event EventHandler<double>? DurationChanged;
    public event EventHandler<bool>? PlayStateChanged;
    public event EventHandler? Ended;
    public event EventHandler<string>? MediaError;

    public VideoPreviewControl()
    {
        BackColor = Color.Black;
        DoubleBuffered = true;

        _web = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.Black
        };
        _message = new Label
        {
            Dock = DockStyle.Fill,
            Text = "正在初始化视频预览…",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = VideoTheme.Muted,
            BackColor = Color.FromArgb(10, 10, 11),
            Font = VideoTheme.Ui9,
            Padding = new Padding(30)
        };
        Controls.Add(_web);
        Controls.Add(_message);
        _message.BringToFront();
    }

    public double Duration => _duration;
    public double Position => _position;
    public bool IsPlaying => _playing;
    public bool IsReady => _ready;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _ = EnsureInitializedAsync();
    }

    private Task EnsureInitializedAsync()
    {
        return _initTask ??= InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MiniMonitor", "VideoWebView");
            Directory.CreateDirectory(userData);
            var options = new CoreWebView2EnvironmentOptions(
                "--autoplay-policy=no-user-gesture-required --disable-background-media-suspend "
                + "--no-proxy-server");
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(
                null, userData, options);
            await _web.EnsureCoreWebView2Async(environment);
            if (_disposed)
            {
                return;
            }
            var settings = _web.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.AreHostObjectsAllowed = false;
            _web.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _readySource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _server = new MediaPreviewServer(PreviewHtml);
                _web.CoreWebView2.Navigate(_server.PageUrl);
            }
            catch (Exception ex)
            {
                VideoLog.Warn("本地媒体预览服务启动失败，退回虚拟主机：" + ex.Message);
                _server?.Dispose();
                _server = null;
                _web.CoreWebView2.NavigateToString(PreviewHtml);
            }
            await _readySource.Task.WaitAsync(TimeSpan.FromSeconds(12));
            if (_disposed)
            {
                return;
            }
            _ready = true;
            _message.Visible = false;
            ApplyVolume(_volume);
        }
        catch (Exception ex)
        {
            VideoLog.Error("视频预览初始化失败", ex);
            if (!_disposed)
            {
                _web.Visible = false;
                _message.Text = "视频预览需要 Microsoft Edge WebView2 Runtime。\n\n"
                    + "请安装 WebView2 Runtime 后重新打开。\n" + ex.Message;
                _message.Visible = true;
                _message.BringToFront();
            }
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            string type = root.TryGetProperty("type", out var typeValue)
                ? typeValue.GetString() ?? "" : "";
            switch (type)
            {
                case "ready":
                    _readySource?.TrySetResult(true);
                    break;
                case "meta":
                    if (TryReadDouble(root, "duration", out double duration) && duration > 0)
                    {
                        _duration = duration;
                        DurationChanged?.Invoke(this, duration);
                    }
                    break;
                case "time":
                    if (TryReadDouble(root, "position", out double position))
                    {
                        _position = position;
                        PositionChanged?.Invoke(this, position);
                    }
                    break;
                case "state":
                    _playing = root.TryGetProperty("playing", out var playing) && playing.GetBoolean();
                    PlayStateChanged?.Invoke(this, _playing);
                    break;
                case "ended":
                    _playing = false;
                    PlayStateChanged?.Invoke(this, false);
                    Ended?.Invoke(this, EventArgs.Empty);
                    break;
                case "error":
                    string message = root.TryGetProperty("message", out var error)
                        ? error.GetString() ?? "无法播放该媒体" : "无法播放该媒体";
                    MediaError?.Invoke(this, message);
                    break;
            }
        }
        catch (Exception ex)
        {
            VideoLog.Warn("预览消息解析失败：" + ex.Message);
        }
    }

    private static bool TryReadDouble(JsonElement element, string name, out double value)
    {
        value = 0;
        return element.TryGetProperty(name, out var property)
            && double.TryParse(property.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    public async Task LoadAsync(string path, bool isImage, double duration, double startSeconds,
        bool autoplay, bool isAudio = false)
    {
        if (_disposed)
        {
            return;
        }
        await _gate.WaitAsync();
        try
        {
            await EnsureInitializedAsync();
            if (_disposed || !_ready || _web.CoreWebView2 == null)
            {
                return;
            }
            string fullPath = Path.GetFullPath(path);
            string url;
            if (_server != null)
            {
                _server.SetMedia(fullPath);
                url = _server.MediaUrl;
            }
            else
            {
                string? directory = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrWhiteSpace(directory))
                {
                    return;
                }
                _web.CoreWebView2.SetVirtualHostNameToFolderMapping(MediaHost, directory,
                    CoreWebView2HostResourceAccessKind.Allow);
                url = "https://" + MediaHost + "/" + Uri.EscapeDataString(Path.GetFileName(fullPath));
            }
            _duration = duration;
            _position = startSeconds;
            string script = "window.mmLoad(" + JsonSerializer.Serialize(url) + ","
                + (isImage ? "true" : "false") + ","
                + (isAudio ? "true" : "false") + ","
                + duration.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + ","
                + startSeconds.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + ","
                + (autoplay ? "true" : "false") + ");";
            await _web.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            VideoLog.Error("加载预览失败：" + path, ex);
            MediaError?.Invoke(this, ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Play()
    {
        Exec("window.mmPlay();");
    }

    public void Pause()
    {
        Exec("window.mmPause();");
    }

    public void Seek(double seconds)
    {
        _position = Math.Max(0, seconds);
        Exec("window.mmSeek(" + _position.ToString("F6",
            System.Globalization.CultureInfo.InvariantCulture) + ");");
    }

    public void FrameStep(double seconds)
    {
        Exec("window.mmFrameStep(" + seconds.ToString("F6",
            System.Globalization.CultureInfo.InvariantCulture) + ");");
    }

    public void SetSpeed(double speed)
    {
        Exec("window.mmSpeed(" + Math.Clamp(speed, 0.05, 16).ToString("F4",
            System.Globalization.CultureInfo.InvariantCulture) + ");");
    }

    public void SetFilter(string css)
    {
        Exec("window.mmFilter(" + JsonSerializer.Serialize(css) + ");");
    }

    public void ApplyVolume(double volume)
    {
        _volume = Math.Clamp(volume, 0, 1);
        Exec("window.mmVolume(" + _volume.ToString("F3",
            System.Globalization.CultureInfo.InvariantCulture) + ");");
    }

    public void Clear(string message)
    {
        _duration = 0;
        _position = 0;
        _playing = false;
        Exec("window.mmClear(" + JsonSerializer.Serialize(message) + ");");
    }

    private void Exec(string script)
    {
        if (_disposed || !_ready || _web.CoreWebView2 == null)
        {
            return;
        }
        try
        {
            _ = _web.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            VideoLog.Warn("预览脚本执行失败：" + ex.Message);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _disposed = true;
            _server?.Dispose();
            _server = null;
            _web.Dispose();
            _gate.Dispose();
        }
        base.Dispose(disposing);
    }

    private const string PreviewHtml = """
<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
html,body{margin:0;padding:0;width:100%;height:100%;background:#0a0a0b;overflow:hidden}
#stage{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;background:#0a0a0b}
video{width:100%;height:100%;object-fit:contain;background:#0a0a0b;outline:none}
img{max-width:100%;max-height:100%;object-fit:contain;display:none}
#overlay{position:absolute;inset:0;display:none;align-items:center;justify-content:center;
color:#9aa0a8;font-family:'Microsoft YaHei UI',sans-serif;font-size:14px;text-align:center;
padding:30px;background:#0a0a0b;white-space:pre-wrap}
</style>
</head>
<body>
<div id="stage">
  <video id="v" playsinline preload="auto"></video>
  <audio id="a" preload="auto" style="display:none"></audio>
  <img id="img" />
  <div id="overlay"></div>
</div>
<script>
(function(){
  const v=document.getElementById('v');
  const a=document.getElementById('a');
  const img=document.getElementById('img');
  const overlay=document.getElementById('overlay');
  let isImage=false;
  let isAudio=false;
  let imageDuration=0;
  let active=v;
  function send(obj){try{window.chrome.webview.postMessage(obj)}catch(e){}}
  function showOverlay(text){overlay.textContent=text;overlay.style.display='flex';}
  function hideOverlay(){overlay.style.display='none';}
  function bind(el){
    el.addEventListener('loadedmetadata',function(){
      send({type:'meta',duration:el.duration||0});
    });
    el.addEventListener('timeupdate',function(){
      send({type:'time',position:el.currentTime||0,duration:el.duration||0});
    });
    el.addEventListener('play',function(){send({type:'state',playing:true})});
    el.addEventListener('pause',function(){send({type:'state',playing:false})});
    el.addEventListener('ended',function(){send({type:'ended'})});
    el.addEventListener('error',function(){
      let message='无法播放该媒体（浏览器不支持的编码或文件损坏）';
      if(el.error){message+='\n错误代码：'+el.error.code;}
      message+='\n网络状态：'+(el.networkState||0);
      if(el.currentSrc){message+='\n源：'+el.currentSrc;}
      showOverlay(message);
      send({type:'error',message:message});
    });
  }
  bind(v);
  bind(a);
  window.mmLoad=function(url,image,audio,duration,start,autoplay){
    isImage=!!image; isAudio=!!audio; imageDuration=duration||0; hideOverlay();
    try{v.pause(); v.removeAttribute('src'); v.load();}catch(e){}
    try{a.pause(); a.removeAttribute('src'); a.load();}catch(e){}
    active = isAudio ? a : v;
    if(isImage){
      v.style.display='none'; a.style.display='none';
      img.style.display='block'; img.src=url;
      send({type:'meta',duration:imageDuration});
      send({type:'time',position:start||0,duration:imageDuration});
      send({type:'state',playing:!!autoplay});
      return;
    }
    img.style.display='none'; img.removeAttribute('src');
    v.style.display=isAudio?'none':'block';
    a.style.display='none';
    active.src=url;
    active.onloadedmetadata=function(){
      try{
        const target=Math.max(0,Math.min((active.duration||0)-0.001,start||0));
        if(target>0){active.currentTime=target;}
      }catch(e){}
      send({type:'meta',duration:active.duration||0});
      if(autoplay){active.play().catch(function(e){send({type:'error',message:'自动播放失败：'+e})})}
    };
    if(isAudio){showOverlay('音频文件');}
  };
  window.mmPlay=function(){
    if(isImage){send({type:'state',playing:true});return;}
    active.play().catch(function(e){send({type:'error',message:'播放失败：'+e})});
  };
  window.mmPause=function(){
    if(isImage){send({type:'state',playing:false});return;}
    active.pause();
  };
  window.mmSeek=function(t){
    if(isImage){send({type:'time',position:t,duration:imageDuration});return;}
    try{ if(isFinite(active.duration)){active.currentTime=Math.max(0,Math.min(active.duration-0.001,t));} }catch(e){}
  };
  window.mmFrameStep=function(dt){
    if(isImage){return;}
    try{ active.pause(); if(isFinite(active.duration)){active.currentTime=Math.max(0,Math.min(active.duration-0.001,(active.currentTime||0)+dt));} }catch(e){}
  };
  window.mmSpeed=function(s){ try{ active.playbackRate=Math.max(0.05,Math.min(16,s)); }catch(e){} };
  window.mmVolume=function(x){ try{ active.volume=Math.max(0,Math.min(1,x)); }catch(e){} };
  window.mmFilter=function(css){ try{ v.style.filter=css||'none'; }catch(e){} };
  window.mmClear=function(text){
    try{ v.pause(); v.removeAttribute('src'); v.load(); }catch(e){}
    try{ a.pause(); a.removeAttribute('src'); a.load(); }catch(e){}
    img.style.display='none'; img.removeAttribute('src');
    isImage=false; isAudio=false; imageDuration=0;
    if(text){showOverlay(text);}else{hideOverlay();}
    send({type:'state',playing:false});
  };
  send({type:'ready'});
})();
</script>
</body>
</html>
""";
}
