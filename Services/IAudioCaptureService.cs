using System;
using System.Collections.Generic;
using System.Text;

namespace Aplicacion_SCA.Services
{
    public interface IAudioCaptureService
    {
        void StartRecording(Action<byte[], int> onAudioDataReceived, bool isEnRodaje = false);
        void StopRecording();
    }
}
