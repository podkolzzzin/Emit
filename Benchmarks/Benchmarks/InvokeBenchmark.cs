using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;

public class SampleClass
{
  public void PrintMessage()
  {
    // Do nothing for the sake of the benchmark
  }
}

public delegate void DynamicMethodDelegate(object target);

[SimpleJob(RuntimeMoniker.NetCoreApp31)]  // .NET Core 3.1
[SimpleJob(RuntimeMoniker.Net50)]         // .NET 5.0
[SimpleJob(RuntimeMoniker.Net60)]         // .NET 6.0
[SimpleJob(RuntimeMoniker.Net70)]         // .NET 6.0
[SimpleJob(RuntimeMoniker.Net48)]         // .NET Framework 4.8
[MemoryDiagnoser]
public class InvokeBenchmark
{
  private readonly SampleClass sampleInstance = new SampleClass();
  private readonly MethodInfo methodInfo;
  private readonly DynamicMethodDelegate dynamicDelegate;
  private readonly Action<SampleClass> expressionDelegate;

  public InvokeBenchmark()
  {
    methodInfo = typeof(SampleClass).GetMethod("PrintMessage");

    // Reflection.Emit
    var dynamicMethod = new DynamicMethod("DynamicInvoke", typeof(void), new Type[] { typeof(object) }, typeof(SampleClass));
    var ilGen = dynamicMethod.GetILGenerator();
    ilGen.Emit(OpCodes.Ldarg_0);
    ilGen.Emit(OpCodes.Castclass, typeof(SampleClass));
    ilGen.Emit(OpCodes.Callvirt, methodInfo);
    ilGen.Emit(OpCodes.Ret);
    dynamicDelegate = (DynamicMethodDelegate)dynamicMethod.CreateDelegate(typeof(DynamicMethodDelegate));

    // Expressions
    var parameter = Expression.Parameter(typeof(SampleClass), "obj");
    var methodCall = Expression.Call(parameter, methodInfo);
    var lambda = Expression.Lambda<Action<SampleClass>>(methodCall, parameter);
    expressionDelegate = lambda.Compile();
  }

  [Benchmark]
  public void MethodInfoInvoke()
  {
    methodInfo.Invoke(sampleInstance, null);
  }

  [Benchmark]
  public void DynamicDelegateInvoke()
  {
    dynamicDelegate(sampleInstance);
  }

  [Benchmark]
  public void ExpressionDelegateInvoke()
  {
    expressionDelegate(sampleInstance);
  }
}