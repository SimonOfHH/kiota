using Kiota.Builder.CodeDOM;
using Kiota.Builder.PathSegmenters;
using Xunit;

namespace Kiota.Builder.Tests.PathSegmenters
{
    public class ALPathSegmenterTests
    {
        private readonly ALPathSegmenter segmenter;
        public ALPathSegmenterTests()
        {
            segmenter = new ALPathSegmenter("D:\\source\\repos\\kiota-sample", "client");
        }

        [Fact]
        public void ALPathSegmenterGeneratesCodeunitFileName()
        {
            var fileName = segmenter.NormalizeFileName(new CodeClass
            {
                Name = "TestClass"
            });
            Assert.Equal("TestClass.Codeunit", fileName);
        }

        [Fact]
        public void ALPathSegmenterStripsPathTraversalFromOriginalName()
        {
            var codeClass = new CodeClass { Name = "Safe" };
            codeClass.CustomData["original-name"] = "../../../../etc/passwd";
            var fileName = segmenter.NormalizeFileName(codeClass);
            // Path traversal sequences must be stripped so the file is written inside the output directory.
            Assert.DoesNotContain("..", fileName);
            Assert.DoesNotContain("/", fileName);
            Assert.DoesNotContain("\\", fileName);
            Assert.Equal("passwd.Codeunit", fileName);
        }

        [Fact]
        public void ALPathSegmenterStripsInvalidFileNameCharactersFromOriginalName()
        {
            var codeEnum = new CodeEnum { Name = "Safe" };
            codeEnum.CustomData["original-name"] = "evil:name*?";
            var fileName = segmenter.NormalizeFileName(codeEnum);
            Assert.DoesNotContain(":", fileName);
            Assert.DoesNotContain("*", fileName);
            Assert.DoesNotContain("?", fileName);
            Assert.Equal("evilname.Enum", fileName);
        }
    }
}
