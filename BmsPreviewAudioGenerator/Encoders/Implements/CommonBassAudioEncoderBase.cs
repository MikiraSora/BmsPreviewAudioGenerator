using ManagedBass.Enc;
using System;
using System.Collections.Generic;
using System.Text;

namespace BmsPreviewAudioGenerator.Encoders.Implements
{
    public abstract class CommonBassAudioEncoderBase : IAudioEncoder
    {
        private int encodeHandle = default;

        public bool IsEncoding => encodeHandle != default;

        public void BeginEncode(EncodeParam param)
        {
            encodeHandle = StartEncoding(param);
        }

        protected abstract int StartEncoding(EncodeParam param);

        public void EndEncode()
        {
            BassEnc.EncodeStop(encodeHandle);
            encodeHandle = default;
        }
    }
}
