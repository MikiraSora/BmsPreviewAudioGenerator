using ManagedBass.Enc;
using System;
using System.Collections.Generic;
using System.Text;

namespace BmsPreviewAudioGenerator.Encoders.Implements
{
    public class OpusAudioEncoder : CommonBassAudioEncoderBase
    {
        protected override int StartEncoding(EncodeParam param)
        {
            return BassEnc_Opus.Start(param.MixHandle, param.EncodeOption, EncodeFlags.AutoFree, param.EncodeOutputFilePath);
        }
    }
}
