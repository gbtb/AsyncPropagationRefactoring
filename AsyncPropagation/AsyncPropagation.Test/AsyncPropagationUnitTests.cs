using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using RoslynTestKit;

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

                public async Task InnerMethodAsync()
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

                public async Task InnerMethodAsync()
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
        
        [Test]
        public void Test_WithInterface()
        {
            var source = @"
            public class Test {

                public async Task [|InnerMethod|]()
                {
                }
            }
            public interface IFoo {
                void Bar();
            }

            public class Foo: IFoo {
                public void Bar(){
                    new Test().InnerMethod();
                }
            }
            ";
            
            var expected = @"
            public class Test {

                public async Task InnerMethodAsync()
                {
                }
            }
            public interface IFoo {
                Task BarAsync();
            }

            public class Foo: IFoo {
                public async Task BarAsync(){
                    await new Test().InnerMethodAsync();
                }
            }
            ";
            
            TestCodeFix(source, expected, _diagId);
        }
        
        
        [Test]
        public void Test_WithBaseClass()
        {
            var source = @"
            public class Test {

                public async Task [|InnerMethod|]()
                {
                }
            }
            public abstract class Foo2 {
                public abstract void Bar();
            }

            public class Foo1: Foo2 {
                public override void Bar()
                {
                    
                }
            }

            public class Foo: Foo1 {
                public override void Bar(){
                    new Test().InnerMethod();
                }
            }
            ";
            
            var expected = @"
            public class Test {

                public async Task InnerMethodAsync()
                {
                }
            }
            public abstract class Foo2 {
                public abstract Task BarAsync();
            }

            public class Foo1: Foo2 {
                public override async Task BarAsync()
                {
                    
                }
            }

            public class Foo: Foo1 {
                public override async Task BarAsync(){
                    await new Test().InnerMethodAsync();
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
