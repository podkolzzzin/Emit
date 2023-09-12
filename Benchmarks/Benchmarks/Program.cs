using BenchmarkDotNet.Running;
using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;

class Program
{
  public static void Main(string[] args) => BenchmarkRunner.Run<InvokeBenchmark>();
}

public interface IInterceptor
{
    void BeforeInvoke(MethodInfo method, object[] args);
    void AfterInvoke(MethodInfo method, object[] args, object result);
}