using ManagedBass.Enc;
using System;
using System.Collections.Generic;
using System.Text;

namespace BmsPreviewAudioGenerator.Encoders.Implements
{
    public class FlacAudioEncoder : CommonBassAudioEncoderBase
    {
        protected override int StartEncoding(EncodeParam param)
        {
            return BassEnc_Flac.Start(param.MixHandle, param.EncodeOption, EncodeFlags.AutoFree, param.EncodeOutputFilePath);
        }
    }
}
