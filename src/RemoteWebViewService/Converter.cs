using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PeakSWC.RemoteableWebView
{
    public static class Converter
    {
        public static MemoryStream ToMemoryStream(this ByteString byteString)
        {
            // Skip making a copy if possible
            if (MemoryMarshal.TryGetArray(byteString.Memory, out var segment))
            {
                if (segment.Array != null)
                    return new MemoryStream(segment.Array, segment.Offset, segment.Count);
                else
                    return new MemoryStream(Array.Empty<byte>());
            }
            else
            {
                return new MemoryStream(byteString.ToByteArray());
            }
        }
    }
}
