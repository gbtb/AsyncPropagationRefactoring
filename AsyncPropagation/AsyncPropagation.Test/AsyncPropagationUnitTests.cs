using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeRefactorings;
using NUnit.Framework;
using RoslynTestKit;

namespace AsyncPropagation.Test
{
    [TestFixture]
    public class AsyncPropagationUnitTest: CodeRefactoringTestFixture
    {
        private string _diagId;
        private AsyncPropagationCodeFixProvider _provider;

        [Test]
        public void Test_OneLevelDeep()
        {
            var source = @"
public class Test {

    public void [|InnerMethod|]()
    {
    }

    public void OuterMethod()
    {
        InnerMethod();
    }
}
            ";
            
            var expected = @"using System.Threading.Tasks;

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
            
            TestCodeRefactoring(source, expected);
        }
        
        [Test]
        public void Test_TwoLevelDeep()
        {
            var source = @"
            using System.Threading.Tasks;

            public class Test {

                public void [|InnerMethod|]()
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
            using System.Threading.Tasks;

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
            
            TestCodeRefactoring(source, expected);
        }
        
        [Test]
        public void Test_WithInterface()
        {
            var source = @"
public class Test {

    public void [|InnerMethod|]()
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
            
            var expected = @"using System.Threading.Tasks;

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
            
            TestCodeRefactoring(source, expected);
        }
        
        [Test]
        public void Test_WithInterfaceAndInheritance()
        {
            var source = @"
public class Test {

    public string [|InnerMethod|]()
    {
    }
}
public interface IInterface1 {
    string Bar();
}

public interface IInterface2: IInterface1 {
    IInterface1 Baz();
}

public class Baz: IInterface2 {
    public virtual string Bar() {
        
    }
}

public class Foo: Baz {
    public override string Bar(){
        var a = base.Bar();
        return new Test().InnerMethod() + a;
    }
}
            ";
            
            var expected = @"using System.Threading.Tasks;

public class Test {

    public async Task<string> InnerMethodAsync()
    {
    }
}
public interface IInterface1 {
    Task<string> BarAsync();
}

public interface IInterface2: IInterface1 {
    IInterface1 Baz();
}

public class Baz: IInterface2 {
    public virtual async Task<string> BarAsync() {
        
    }
}

public class Foo: Baz {
    public override async Task<string> BarAsync(){
        var a = await base.BarAsync();
        return await new Test().InnerMethodAsync() + a;
    }
}
            ";
            
            TestCodeRefactoring(source, expected);
        }
        
        
        [Test]
        public void Test_WithBaseClass()
        {
            var source = @"
public class Test {

    public void [|InnerMethod|]()
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
            
            var expected = @"using System.Threading.Tasks;

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
            
            TestCodeRefactoring(source, expected);
        }

        protected override string LanguageName => LanguageNames.CSharp;

        protected override CodeRefactoringProvider CreateProvider()
        {
            return _provider;
        }

        protected override bool FailWhenInputContainsErrors => false;

        [OneTimeSetUp]
        public void SetUp()
        {
            _provider = new AsyncPropagationCodeFixProvider();
        }
    }
}
