using System;
using System.IO;
using KeePassLib.Cryptography.KeyDerivation;

namespace LockPad.Core
{
    /// <summary>
    /// Reads the KDF parameters from a KDBX 4 file's outer header without deriving a key or decrypting anything. Used
    /// to decide whether a file is cheap enough to test-open (NotebookStore's .bak ownership check).
    /// </summary>
    internal static class KdbxHeader
    {
        private const uint Signature1 = 0x9AA2D903;
        private const uint Signature2 = 0xB54BFB67;
        private const uint MajorVersionMask = 0xFFFF0000;
        private const uint MajorVersion4 = 0x00040000;
        private const byte EndOfHeaderField = 0;
        private const byte TransformSeedField = 5;
        private const byte TransformRoundsField = 6;
        private const byte KdfParametersField = 11;
        /// <summary>A LockPad header is a few hundred bytes; anything larger is not read.</summary>
        private const int MaxHeaderBytes = 1024 * 1024;

        /// <summary>The file's KDF parameters, or null when it is not a KDBX 4.x file with exactly one readable KDF
        /// parameters field and no obsolete transform fields (KDBX 3.x, another format, truncated, damaged or oversized
        /// header). I/O errors propagate.</summary>
        public static KdfParameters ReadKdfParameters(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return ReadKdfParameters(fs);
        }

        /// <summary>As <see cref="ReadKdfParameters(string)"/>, from a file's bytes already read.</summary>
        public static KdfParameters ReadKdfParameters(byte[] file)
        {
            using (var ms = new MemoryStream(file, false))
                return ReadKdfParameters(ms);
        }

        private static KdfParameters ReadKdfParameters(Stream stream)
        {
            using (var br = new BinaryReader(stream))
            {
                try
                {
                    if (br.ReadUInt32() != Signature1 || br.ReadUInt32() != Signature2) return null;
                    if ((br.ReadUInt32() & MajorVersionMask) != MajorVersion4) return null;

                    long read = 12;
                    KdfParameters kdf = null;
                    bool seenKdf = false;
                    while (true)
                    {
                        byte id = br.ReadByte();
                        int size = br.ReadInt32();
                        read += 5;
                        if (size < 0 || read + size > MaxHeaderBytes) return null;
                        byte[] data = br.ReadBytes(size);
                        if (data.Length != size) return null;
                        read += size;
                        if (id == EndOfHeaderField) return kdf;
                        // KeePassLib derives with the LAST KDF parameters field, and in any version also honours the
                        // obsolete KDBX 3 transform seed and rounds fields (they switch to AES-KDF). A header with any
                        // of these besides one KDF field is not ours: null, so it is never test-opened (full-codebase
                        // red team 2026-10-22).
                        if (id == TransformSeedField || id == TransformRoundsField) return null;
                        if (id == KdfParametersField)
                        {
                            if (seenKdf) return null;
                            seenKdf = true;
                            kdf = Deserialize(data);
                        }
                    }
                }
                catch (EndOfStreamException)
                {
                    return null;
                }
            }
        }

        private static KdfParameters Deserialize(byte[] data)
        {
            try
            {
                return KdfParameters.DeserializeExt(data);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                return null;
            }
        }
    }
}
