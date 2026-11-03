using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using LockPad.App;
using NUnit.Framework;

namespace LockPad.Tests
{
    /// <summary>UX round 3, V6: the application icon (note page with padlock).</summary>
    [TestFixture]
    public class IconTests
    {
        private const string IconPath = @"Assets\LockPad.ico";
        private static readonly int[] Sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

        private static string RepoRoot()
        {
            DirectoryInfo d = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "LockPad.sln"))) d = d.Parent;
            Assert.That(d, Is.Not.Null, "LockPad.sln not found");
            return d.FullName;
        }

        private static string AppDir { get { return Path.Combine(RepoRoot(), "src", "LockPad.App"); } }

        [Test]
        public void IconFile_HasAllNineSizes_EachAPngOfThatSize()
        {
            string path = Path.Combine(AppDir, IconPath);
            Assert.That(File.Exists(path), Is.True, path);
            byte[] ico = File.ReadAllBytes(path);

            Assert.That(BitConverter.ToUInt16(ico, 0), Is.EqualTo(0), "reserved");
            Assert.That(BitConverter.ToUInt16(ico, 2), Is.EqualTo(1), "type: icon");
            int count = BitConverter.ToUInt16(ico, 4);
            var found = new List<int>();
            for (int i = 0; i < count; i++)
            {
                int e = 6 + 16 * i;
                int w = ico[e] == 0 ? 256 : ico[e];
                int h = ico[e + 1] == 0 ? 256 : ico[e + 1];
                Assert.That(h, Is.EqualTo(w), "square");
                int length = (int)BitConverter.ToUInt32(ico, e + 8);
                int offset = (int)BitConverter.ToUInt32(ico, e + 12);
                Assert.That(offset + length, Is.LessThanOrEqualTo(ico.Length));
                using (var ms = new MemoryStream(ico, offset, length))
                {
                    BitmapFrame frame = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat,
                        BitmapCacheOption.OnLoad).Frames[0];
                    Assert.That(frame.PixelWidth, Is.EqualTo(w), "entry " + w + " decodes to its size");
                    Assert.That(frame.PixelHeight, Is.EqualTo(w));
                }
                found.Add(w);
            }
            Assert.That(found.OrderBy(s => s), Is.EqualTo(Sizes));
        }

        [Test]
        public void Csproj_SetsApplicationIcon_AndEmbedsItForTheWindows()
        {
            XDocument doc = XDocument.Load(Path.Combine(AppDir, "LockPad.App.csproj"));
            Assert.That(doc.Descendants("ApplicationIcon").Select(e => e.Value.Trim()), Is.EqualTo(new[] { IconPath }));
            Assert.That(doc.Descendants("Resource").Select(e => (string)e.Attribute("Include")), Does.Contain(IconPath));
        }

        [Test]
        public void EveryWindowXaml_SetsTheIcon()
        {
            var files = new List<string> { Path.Combine(AppDir, "MainWindow.xaml") };
            files.AddRange(Directory.GetFiles(Path.Combine(AppDir, "Views"), "*Window.xaml"));
            Assert.That(files.Count, Is.GreaterThanOrEqualTo(5));
            foreach (string f in files)
            {
                XElement root = XDocument.Load(f).Root;
                Assert.That((string)root.Attribute("Icon"), Is.EqualTo("/LockPad;component/Assets/LockPad.ico"),
                    Path.GetFileName(f));
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void MainWindow_IconIsSet()
        {
            System.IO.Packaging.PackUriHelper.Create(new Uri("application:///"));
            var window = new MainWindow();
            try
            {
                Assert.That(window.Icon, Is.Not.Null);
                var frame = window.Icon as BitmapFrame;
                Assert.That(frame, Is.Not.Null);
                Assert.That(frame.Decoder.Frames.Count, Is.EqualTo(Sizes.Length), "the multi-size .ico");
            }
            finally
            {
                window.Close();
            }
        }
    }
}
