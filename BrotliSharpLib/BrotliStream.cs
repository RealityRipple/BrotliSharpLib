using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using size_t = BrotliSharpLib.Brotli.SizeT;

namespace BrotliSharpLib
{
    /// <summary>
    /// Represents a Brotli stream for compression or decompression.
    /// </summary>
    public unsafe class BrotliStream : Stream {
        private Stream _stream;
        private bool _leaveOpen, _disposed;
        private IntPtr _customDictionary = IntPtr.Zero;
        private byte[] _buffer;
        private int _bufferCount, _bufferOffset;

        private Brotli.BrotliDecoderStateStruct _decoderState;

        private Brotli.BrotliDecoderResult _lastDecoderState =
            Brotli.BrotliDecoderResult.BROTLI_DECODER_RESULT_NEEDS_MORE_INPUT;

        /// <summary>
        /// Initializes a new instance of the <see cref="BrotliStream"/> class using the specified stream and
        /// compression mode, and optionally leaves the stream open.
        /// </summary>
        /// <param name="stream">The stream to compress or decompress.</param>
        /// <param name="mode">One of the enumeration values that indicates whether to compress or decompress the stream.</param>
        /// <param name="leaveOpen"><c>true</c> to leave the stream open after disposing the <see cref="BrotliStream"/> object; otherwise, <c>false</c>.</param>
        public BrotliStream(Stream stream, CompressionMode mode, bool leaveOpen) {
            if (stream == null)
                throw new ArgumentNullException("stream");

            if (CompressionMode.Compress != mode && CompressionMode.Decompress != mode)
                throw new ArgumentOutOfRangeException("mode");

            _stream = stream;
            _leaveOpen = leaveOpen;

            if (!_stream.CanRead)
                throw new ArgumentException("Stream does not support read", "stream");

            _decoderState = Brotli.BrotliCreateDecoderState();
            Brotli.BrotliDecoderStateInit(ref _decoderState);
            _buffer = new byte[0xfff0];
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BrotliStream"/> class using the specified stream and
        /// compression mode.
        /// </summary>
        /// <param name="stream">The stream to compress or decompress.</param>
        /// <param name="mode">One of the enumeration values that indicates whether to compress or decompress the stream.</param>
        public BrotliStream(Stream stream, CompressionMode mode) :
            this(stream, mode, false) {
        }

        /// <summary>
        /// Ensures that resources are freed and other cleanup operations are performed when the garbage collector reclaims the <see cref="BrotliStream"/>.
        /// </summary>
        ~BrotliStream()
        {
            Dispose(false);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="BrotliStream"/> and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing"></param>
        protected override void Dispose(bool disposing) {
            if (!_disposed) {

                Brotli.BrotliDecoderStateCleanup(ref _decoderState);
                if (_customDictionary != IntPtr.Zero) {
                    Marshal.FreeHGlobal(_customDictionary);
                    _customDictionary = IntPtr.Zero;
                }
                _disposed = true;
            }

            if (disposing && !_leaveOpen && _stream != null) {
                _stream.Dispose();
                _stream = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Flushes any buffered data into the stream
        /// </summary>
        public override void Flush() {
            EnsureNotDisposed();
            FlushCompress(false);
        }

        private void FlushCompress(bool finish) {
            return;
        }

        /// <summary>
        /// This operation is not supported and always throws a <see cref="NotSupportedException"/>.
        /// </summary>
        public override long Seek(long offset, SeekOrigin origin) {
            throw new NotSupportedException();
        }

        /// <summary>
        /// This operation is not supported and always throws a <see cref="NotSupportedException"/>.
        /// </summary>
        public override void SetLength(long value) {
            throw new NotSupportedException();
        }

        private void ValidateParameters(byte[] array, int offset, int count) {
            if (array == null)
                throw new ArgumentNullException("array");

            if (offset < 0)
                throw new ArgumentOutOfRangeException("offset");

            if (count < 0)
                throw new ArgumentOutOfRangeException("count");

            if (array.Length - offset < count)
                throw new ArgumentException("Invalid argument offset and count");
        }

        /// <summary>
        /// Reads a number of decompressed bytes into the specified byte array.
        /// </summary>
        /// <param name="buffer">The array to store decompressed bytes.</param>
        /// <param name="offset">The byte offset in <paramref name="buffer"/> at which the read bytes will be placed.</param>
        /// <param name="count">The maximum number of decompressed bytes to read.</param>
        /// <returns>The number of bytes that were read into the byte array.</returns>
        public override int Read(byte[] buffer, int offset, int count) {
            EnsureNotDisposed();
            ValidateParameters(buffer, offset, count);

            int totalWritten = 0;
            while (offset < buffer.Length && _lastDecoderState != Brotli.BrotliDecoderResult.BROTLI_DECODER_RESULT_SUCCESS) {
                if (_lastDecoderState == Brotli.BrotliDecoderResult.BROTLI_DECODER_RESULT_NEEDS_MORE_INPUT) {
                    if (_bufferCount > 0 && _bufferOffset != 0) {
                        Array.Copy(_buffer, _bufferOffset, _buffer, 0, _bufferCount);
                    }
                    _bufferOffset = 0;

                    int numRead = 0;
                    while (_bufferCount < _buffer.Length && ((numRead = _stream.Read(_buffer, _bufferCount, _buffer.Length - _bufferCount)) > 0)) {
                        _bufferCount += numRead;
                        if (_bufferCount > _buffer.Length)
                            throw new InvalidDataException("Invalid input stream detected, more bytes supplied than expected.");
                    }

                    if (_bufferCount <= 0)
                        break;
                }

                size_t available_in = _bufferCount;
                size_t available_in_old = available_in;
                size_t available_out = count;
                size_t available_out_old = available_out;

                fixed (byte* out_buf_ptr = buffer)
                fixed (byte* in_buf_ptr = _buffer) {
                    byte* in_buf = in_buf_ptr + _bufferOffset;
                    byte* out_buf = out_buf_ptr + offset;
                    _lastDecoderState = Brotli.BrotliDecoderDecompressStream(ref _decoderState, &available_in, &in_buf,
                        &available_out, &out_buf, null);
                }

                if (_lastDecoderState == Brotli.BrotliDecoderResult.BROTLI_DECODER_RESULT_ERROR)
                    throw new InvalidDataException("Decompression failed with error code: " + _decoderState.error_code);

                size_t bytesConsumed = available_in_old - available_in;
                size_t bytesWritten = available_out_old - available_out;

                if (bytesConsumed > 0) {
                    _bufferOffset += (int) bytesConsumed;
                    _bufferCount -= (int) bytesConsumed;
                }

                if (bytesWritten > 0) {
                    totalWritten += (int)bytesWritten;
                    offset += (int)bytesWritten;
                    count -= (int)bytesWritten;
                }
            }

            return totalWritten;
        }

        /// <summary>
        /// Writes compressed bytes to the underlying stream from the specified byte array.
        /// </summary>
        /// <param name="buffer">The buffer that contains the data to compress.</param>
        /// <param name="offset">The byte offset in <paramref name="buffer"/> from which the bytes will be read.</param>
        /// <param name="count">The maximum number of bytes to write.</param>
        public override void Write(byte[] buffer, int offset, int count) {
            throw new InvalidOperationException("Write is only supported in Compress mode");
        }

        /// <summary>
        /// Gets a value indicating whether the stream supports reading while decompressing a file.
        /// </summary>
        public override bool CanRead {
            get {
                if (_stream == null)
                    return false;

                return _stream.CanRead;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the stream supports seeking.
        /// </summary>
        public override bool CanSeek
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the stream supports writing.
        /// </summary>
        public override bool CanWrite {
            get {
                return false;
            }
        }

        /// <summary>
        /// This property is not supported and always throws a <see cref="NotSupportedException"/>.
        /// </summary>
        public override long Length {
            get {
                throw new NotSupportedException();
            }
        }

        /// <summary>
        /// This property is not supported and always throws a <see cref="NotSupportedException"/>.
        /// </summary>
        public override long Position {
            get {
                throw new NotSupportedException();
            }
            set {
                throw new NotSupportedException();
            }
        }

        private void EnsureNotDisposed() {
            if (_stream == null)
                throw new ObjectDisposedException(null, "The underlying stream has been disposed");

            if (_disposed)
                throw new ObjectDisposedException(null);
        }
    }
}
