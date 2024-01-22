using System;
using System.IO;
using System.Runtime.InteropServices;
using size_t = BrotliSharpLib.Brotli.SizeT;

namespace BrotliSharpLib {
    /// <summary>
    /// A class for compressing and decompressing data using the brotli algorithm.
    /// </summary>
    public static partial class Brotli {
        /// <summary>
        /// Decompresses a byte array into a new byte array using brotli.
        /// </summary>
        /// <param name="buffer">The byte array to decompress.</param>
        /// <param name="offset">The byte offset in <paramref name="buffer"/> to read from.</param>
        /// <param name="length">The number of bytes to read.</param>
        /// <param name="customDictionary">The custom dictionary that will be passed to the decoder</param>
        /// <returns>The byte array in compressed form</returns>
        public static unsafe byte[] DecompressBuffer(byte[] buffer, int offset, int length, byte[] customDictionary = null) {
            if (buffer == null)
                throw new ArgumentNullException("buffer");

            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset");

            if (length < 0)
                throw new ArgumentOutOfRangeException("length");

            if (offset + length > buffer.Length)
                throw new IndexOutOfRangeException("Offset and length exceed the range of the buffer");

            using (var ms = new MemoryStream()) {
                // Create the decoder state and intialise it.
                var s = BrotliCreateDecoderState();
                BrotliDecoderStateInit(ref s);

                // Set the custom dictionary
                GCHandle dictionaryHandle = customDictionary != null ? GCHandle.Alloc(customDictionary, GCHandleType.Pinned) : default(GCHandle);
                if (customDictionary != null)
                    BrotliDecoderSetCustomDictionary(ref s, customDictionary.Length, (byte*) dictionaryHandle.AddrOfPinnedObject());

                // Create a 64k buffer to temporarily store decompressed contents.
                byte[] writeBuf = new byte[0x10000];

                // Pin the output buffer and the input buffer.
                fixed (byte* outBuffer = writeBuf) {
                    fixed (byte* inBuffer = buffer) {
                        // Specify the length of the input buffer.
                        size_t len = length;

                        // Local vars for input/output buffer.
                        var bufPtr = inBuffer + offset;
                        var outPtr = outBuffer;

                        // Specify the amount of bytes available in the output buffer.
                        size_t availOut = writeBuf.Length;

                        // Total number of bytes decoded.
                        size_t total = 0;

                        // Main decompression loop.
                        BrotliDecoderResult result;
                        while (
                            (result =
                                BrotliDecoderDecompressStream(ref s, &len, &bufPtr, &availOut, &outPtr, &total)) ==
                            BrotliDecoderResult.BROTLI_DECODER_RESULT_NEEDS_MORE_OUTPUT) {
                            ms.Write(writeBuf, 0, (int) (writeBuf.Length - availOut));
                            availOut = writeBuf.Length;
                            outPtr = outBuffer;
                        }

                        // Check the result and write final block.
                        if (result == BrotliDecoderResult.BROTLI_DECODER_RESULT_SUCCESS)
                            ms.Write(writeBuf, 0, (int) (writeBuf.Length - availOut));

                        // Cleanup and throw.
                        BrotliDecoderStateCleanup(ref s);
                        if (customDictionary != null)
                            dictionaryHandle.Free();

                        if (result != BrotliDecoderResult.BROTLI_DECODER_RESULT_SUCCESS)
                            throw new InvalidDataException("Decompress failed with error code: " + s.error_code);

                        return ms.ToArray();
                    }
                }
            }
        }
    }
}