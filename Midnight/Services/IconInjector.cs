using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Midnight.Services
{
    public static class IconInjector
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr BeginUpdateResource(string pFileName, bool bDeleteExistingResources);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool UpdateResource(IntPtr hUpdate, IntPtr lpType, IntPtr lpName, ushort wLanguage, byte[]? lpData, uint cbData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool EndUpdateResource(IntPtr hUpdate, bool fDiscard);

        private const ushort RT_ICON = 3;
        private const ushort RT_GROUP_ICON = 14;
        private const ushort LANG_NEUTRAL = 0;

        public static void InjectIcon(string exePath, string iconPath)
        {
            if (!File.Exists(exePath)) throw new FileNotFoundException("Target exe not found.", exePath);
            if (!File.Exists(iconPath)) throw new FileNotFoundException("Icon file not found.", iconPath);

            string ext = Path.GetExtension(iconPath).ToLowerInvariant();
            if (ext != ".ico") throw new ArgumentException("Only .ico files are supported for icon injection.");

            byte[] icoData = File.ReadAllBytes(iconPath);
            var (groupHeader, iconImages) = ParseIco(icoData);

            IntPtr handle = BeginUpdateResource(exePath, false);
            if (handle == IntPtr.Zero)
                throw new InvalidOperationException($"BeginUpdateResource failed: {Marshal.GetLastWin32Error()}");

            bool success = false;
            try
            {
                for (int i = 0; i < iconImages.Count; i++)
                {
                    UpdateResource(handle, (IntPtr)RT_ICON, (IntPtr)(i + 1), LANG_NEUTRAL,
                        iconImages[i], (uint)iconImages[i].Length);
                }

                UpdateResource(handle, (IntPtr)RT_GROUP_ICON, (IntPtr)1, LANG_NEUTRAL,
                    groupHeader, (uint)groupHeader.Length);

                success = true;
            }
            finally
            {
                EndUpdateResource(handle, !success);
            }
        }

        private static (byte[] groupHeader, System.Collections.Generic.List<byte[]> images) ParseIco(byte[] icoData)
        {
            using var ms = new MemoryStream(icoData);
            using var br = new BinaryReader(ms);

            br.ReadUInt16(); // reserved
            br.ReadUInt16(); // type (1 = icon)
            int count = br.ReadUInt16();

            var entries = new (int width, int height, int colorCount, int planes, int bitCount, int dataSize, int dataOffset)[count];
            for (int i = 0; i < count; i++)
            {
                int w = br.ReadByte();
                int h = br.ReadByte();
                int colorCount = br.ReadByte();
                br.ReadByte(); // reserved
                int planes = br.ReadUInt16();
                int bitCount = br.ReadUInt16();
                int dataSize = (int)br.ReadUInt32();
                int dataOffset = (int)br.ReadUInt32();
                entries[i] = (w, h, colorCount, planes, bitCount, dataSize, dataOffset);
            }

            var images = new System.Collections.Generic.List<byte[]>();
            foreach (var entry in entries)
            {
                var img = new byte[entry.dataSize];
                Array.Copy(icoData, entry.dataOffset, img, 0, entry.dataSize);
                images.Add(img);
            }

            // Build RT_GROUP_ICON resource
            using var headerMs = new MemoryStream();
            using var bw = new BinaryWriter(headerMs);

            bw.Write((ushort)0); // reserved
            bw.Write((ushort)1); // type
            bw.Write((ushort)count);

            for (int i = 0; i < count; i++)
            {
                var e = entries[i];
                bw.Write((byte)(e.width == 0 ? 256 : e.width));
                bw.Write((byte)(e.height == 0 ? 256 : e.height));
                bw.Write((byte)e.colorCount);
                bw.Write((byte)0); // reserved
                bw.Write((ushort)e.planes);
                bw.Write((ushort)e.bitCount);
                bw.Write((uint)e.dataSize);
                bw.Write((ushort)(i + 1)); // ordinal ID
            }

            return (headerMs.ToArray(), images);
        }
    }
}

