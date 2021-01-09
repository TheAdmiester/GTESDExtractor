using Syroot.BinaryData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GTESDExtractor
{
    class Program
    {
        struct GTED 
        {
            public string name;
            public byte[] sxdfData;
            public List<float> rpms;
            public List<byte[]> mprhEntries;
        }

        static void Main(string[] args)
        {
            // Header position can vary, simply define it here to search for it later
            byte[] mprhHeader = new byte[] { 0x4D, 0x52, 0x50, 0x48 };
            bool stillReading = true;
            List<GTED> gteds = new List<GTED>();

            string FullArgs = "";

            if (args[0] != null)
            {
                // Combining args, probably very hacky
                for (int i = 0; i < args.Length; i++)
                {
                    FullArgs = FullArgs + " " + args[i];
                }

                if (FullArgs.Split('.')[1] == "GTESD")
                {
                    string fileDirectory = Path.GetDirectoryName(FullArgs);
                    var bytes = File.ReadAllBytes(FullArgs);

                    using (var stream = new BinaryStream(new MemoryStream(bytes)))
                    {
                        if (stream.ReadString(4) == "GTEP")
                        {
                            GetGTEDInfo(stream);

                            foreach (GTED gted in gteds)
                            {
                                DirectoryInfo newDir = Directory.CreateDirectory(Path.Combine(fileDirectory, Path.GetFileNameWithoutExtension(args[0])));

                                File.WriteAllBytes(Path.Combine(newDir.FullName, string.Format("{0}.sxd", gted.name)), gted.sxdfData);

                                // Aes and Swt sounds never appear to contain any valid sxd data
                                if (!gted.name.Contains("Aes") && !gted.name.Contains("Swt"))
                                {
                                    using (var sw = new StreamWriter(Path.Combine(newDir.FullName, string.Format("{0}_rpms.txt", gted.name))))
                                    {
                                        foreach (float rpm in gted.rpms)
                                        {
                                            sw.WriteLine(rpm);
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("Not a valid GTESD file.");
                            Console.ReadKey();
                        }
                    }
                }
            }

            int FindSequence(byte[] source, byte[] seq)
            {
                var start = -1;
                for (var i = 0; i < source.Length - seq.Length + 1 && start == -1; i++)
                {
                    var j = 0;
                    for (; j < seq.Length && source[i + j] == seq[j]; j++) { }
                    if (j == seq.Length) start = i;
                }
                return start;
            }

            List<byte[]> GetMPRHInfo(byte[] sxdfData)
            {
                int mprhOffset = FindSequence(sxdfData, mprhHeader);
                int mprhEntries = 0;
                List<byte[]> mprhs = new List<byte[]>();
                int chunkSize = 0;

                using (var stream = new BinaryStream(new MemoryStream(sxdfData)))
                {
                    stream.Position = mprhOffset + 4;

                    chunkSize = stream.ReadInt32();

                    byte[] mprhData = stream.ReadBytes(chunkSize);

                    using (var mprhStream = new BinaryStream(new MemoryStream(mprhData)))
                    {
                        mprhStream.Position += 56;

                        mprhEntries = mprhStream.ReadInt32();
                        long mprhStartOffset = mprhStream.Position;

                        for (int i = 0; i < mprhEntries; i++)
                        {
                            int currentMprhOffset = mprhStream.ReadInt32();

                            mprhStream.Position += (currentMprhOffset - 4);
                            mprhs.Add(mprhStream.ReadBytes(64));

                            mprhStream.Position -= 64 + (currentMprhOffset - 4);
                        }
                    }
                }

                return mprhs;
            }

            void GetGTEDInfo(BinaryStream stream)
            {
                int chunkSize = 0;

                stream.Position = 0x180;

                while (stillReading)
                {
                    if (stream.ReadString(4) == "GTED")
                    {
                        GTED gted = new GTED();

                        chunkSize = stream.ReadInt32();

                        stream.Position += 8;

                        gted.name = stream.ReadString(11);

                        stream.Position -= 27;

                        if (stream.Position + chunkSize < stream.Length) // This ignores the last GTED, but doesn't seem to be a valid one anyway
                        {
                            stream.Position += 128;

                            gted.sxdfData = stream.ReadBytes(chunkSize - 128);

                            if (!gted.name.Contains("Aes") && !gted.name.Contains("Swt"))
                            {
                                gted.mprhEntries = GetMPRHInfo(gted.sxdfData);

                                int counter = 0;
                                List<float> rpmEntries = new List<float>();

                                foreach (byte[] mprhEntry in gted.mprhEntries)
                                {
                                    using (var mprhStream = new BinaryStream(new MemoryStream(mprhEntry)))
                                    {
                                        if (counter == 0)
                                        {
                                            mprhStream.Position = 20;

                                            rpmEntries.Add(BitConverter.ToSingle(mprhStream.ReadBytes(4), 0));
                                        }
                                        else
                                        {
                                            mprhStream.Position = 12;

                                            rpmEntries.Add(BitConverter.ToSingle(mprhStream.ReadBytes(4), 0));
                                        }
                                    }

                                    counter++;
                                }

                                gted.rpms = rpmEntries;
                            }

                            gteds.Add(gted);
                        }
                        else
                        {
                            stillReading = false;
                        }
                    }
                    else
                    {
                        stillReading = false;
                    }
                }
            }
        }
    }
}
