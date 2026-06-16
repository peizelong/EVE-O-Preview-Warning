using System;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;

namespace EveOPreview.Services.Detection
{
    public class AlarmPlayer : IDisposable
    {
        [DllImport("winmm.dll")]
        private static extern bool PlaySound(string pszSound, IntPtr hmod, uint fdwSound);

        private const uint SND_ASYNC = 0x0001;
        private const uint SND_LOOP = 0x0008;
        private const uint SND_PURGE = 0x0040;

        private SoundPlayer _soundPlayer;
        private string _customSoundPath;
        private bool _isPlaying;
        private bool _disposed;

        public bool IsPlaying => _isPlaying;

        public string CustomSoundPath
        {
            get => _customSoundPath;
            set
            {
                _customSoundPath = value;
                if (!string.IsNullOrEmpty(value) && File.Exists(value))
                {
                    try
                    {
                        _soundPlayer = new SoundPlayer(value);
                        _soundPlayer.Load();
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"[Alarm] 加载自定义声音失败: {ex.Message}");
                    }
                }
            }
        }

        public void PlayAlert()
        {
            if (_isPlaying) return;
            _isPlaying = true;
            try
            {
                if (_soundPlayer != null)
                {
                    _soundPlayer.PlayLooping();
                }
                else
                {
                    SystemSounds.Exclamation.Play();
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[Alarm] 播放警报失败: {ex.Message}");
            }
        }

        public void StopAlert()
        {
            if (!_isPlaying) return;
            _isPlaying = false;
            try
            {
                if (_soundPlayer != null)
                {
                    _soundPlayer.Stop();
                }
                else
                {
                    PlaySound(null, IntPtr.Zero, SND_PURGE);
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[Alarm] 停止警报失败: {ex.Message}");
            }
        }

        public void PlayTestSound()
        {
            try
            {
                if (_soundPlayer != null) _soundPlayer.Play();
                else SystemSounds.Beep.Play();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[Alarm] 播放测试声音失败: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            StopAlert();
            _soundPlayer?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
