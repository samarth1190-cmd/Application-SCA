using System;
using System.Threading.Tasks;
using Android.Content;
using Android.Media;
using Aplicacion_SCA.Services;

namespace Aplicacion_SCA.Platforms.Android
{
    public class AudioCaptureService : IAudioCaptureService
    {
        private AudioRecord? _audioRecord;
        private bool _isRecording;
        private const int SampleRate = 16000;

        private AudioManager? _audioManager;
        private bool _usandoBluetoothSco = false; 

        public void StartRecording(Action<byte[], int> onAudioDataReceived, bool isEnRodaje = false)
        {
            if (_isRecording) return;

            _audioManager = (AudioManager?)global::Android.App.Application.Context.GetSystemService(Context.AudioService);

            
            if (!isEnRodaje)
            {
                
                if (_audioManager != null)
                {
                    _audioManager.Mode = Mode.InCommunication;
                    _audioManager.StartBluetoothSco();
                    _audioManager.BluetoothScoOn = true;
                    _usandoBluetoothSco = true;
                }
            }
            else
            {
                
                _usandoBluetoothSco = false;
            }

            
            AudioSource source = AudioSource.Mic;

            int bufferSize = AudioRecord.GetMinBufferSize(SampleRate, ChannelIn.Mono, Encoding.Pcm16bit);

            _audioRecord = new AudioRecord(source, SampleRate, ChannelIn.Mono, Encoding.Pcm16bit, bufferSize * 2);

            _isRecording = true;
            _audioRecord.StartRecording();

            Task.Run(() =>
            {
                byte[] buffer = new byte[bufferSize * 2];

                while (_isRecording && _audioRecord != null)
                {
                    int read = _audioRecord.Read(buffer, 0, buffer.Length);
                    if (read > 0)
                    {
                        onAudioDataReceived(buffer, read);
                    }
                }
            });
        }

        public void StopRecording()
        {
            _isRecording = false;

            if (_audioRecord != null)
            {
                _audioRecord.Stop();
                _audioRecord.Release();
                _audioRecord = null;
            }

            
            if (_audioManager != null && _usandoBluetoothSco)
            {
                _audioManager.StopBluetoothSco();
                _audioManager.BluetoothScoOn = false;
                _audioManager.Mode = Mode.Normal;
                _usandoBluetoothSco = false;
            }
        }
    }
}