using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using RoslynTestKit;
using VerifyCS = AsyncPropagation.Test.CSharpCodeFixVerifier<
    AsyncPropagation.AsyncPropagationAnalyzer,
    AsyncPropagation.AsyncPropagationCodeFixProvider>;

namespace AsyncPropagation.Test
{
    [TestFixture]
    public class AsyncPropagationUnitTest: CodeFixTestFixture
    {
        private string _diagId;
        private AsyncPropagationCodeFixProvider _provider;

        [Test]
        public void Test_OneLevelDeep()
        {
            var source = @"
            public class Test {

                public async Task [|InnerMethod|]()
                {
                }

                public void OuterMethod()
                {
                    InnerMethod();
                }
            }
            ";
            
            var expected = @"
            public class Test {

                public async Task InnerMethod()
                {
                }

                public async Task OuterMethodAsync()
                {
                    await InnerMethodAsync();
                }
            }
            ";
            
            TestCodeFix(source, expected, _diagId);
        }
        
        [Test]
        public void Test_TwoLevelDeep()
        {
            var source = @"
            public class Test {

                public async Task [|InnerMethod|]()
                {
                }

                public int OuterMethod(int i)
                {
                    InnerMethod();
                    return i + 1;
                }

                private int TopLevelMethod()
                {
                    if (OuterMethod(2) == 3){
                        return 0    
                    }else {
                        InnerMethod();
                    }
                    
                }
            }
            ";
            
            var expected = @"
            public class Test {

                public async Task InnerMethod()
                {
                }

                public async Task<int> OuterMethodAsync(int i)
                {
                    await InnerMethodAsync();
                    return i + 1;
                }

                private async Task<int> TopLevelMethodAsync()
                {
                    if (await OuterMethodAsync(2) == 3){
                        return 0    
                    }else {
                        await InnerMethodAsync();
                    }
                    
                }
            }
            ";
            
            TestCodeFix(source, expected, _diagId);
        }

        protected override string LanguageName => LanguageNames.CSharp;
        
        protected override IReadOnlyCollection<DiagnosticAnalyzer> CreateAdditionalAnalyzers() => new[] { new AsyncPropagationAnalyzer() };

        protected override CodeFixProvider CreateProvider()
        {
            return _provider;
        }

        [OneTimeSetUp]
        public void SetUp()
        {
            _provider = new AsyncPropagationCodeFixProvider();
            _diagId = _provider.FixableDiagnosticIds.First();
        }
    }
}
