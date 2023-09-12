namespace Benchmarks
{
  using System;
  using System.Linq;
  using System.Reflection;
  using System.Reflection.Emit;

  public class ProxyTypeBuilder<T>
  {
    private readonly TypeBuilder _typeBuilder;
    private readonly FieldBuilder _realObjectField,
      _interceptorField;
    public ProxyTypeBuilder(ModuleBuilder moduleBuilder)
    {
      _typeBuilder = moduleBuilder.DefineType($"{typeof(T).Name}Proxy", TypeAttributes.Public);
      _typeBuilder.AddInterfaceImplementation(typeof(T));

      _realObjectField = _typeBuilder.DefineField("_realObject", typeof(T), FieldAttributes.Private);
      _interceptorField = _typeBuilder.DefineField("_interceptor", typeof(IInterceptor), FieldAttributes.Private);
    }
    
    public void GenerateMethods()
    {
      foreach (var method in typeof(T).GetMethods())
      {
        var parameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
        var methodBuilder = _typeBuilder.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual, method.ReturnType, parameterTypes);
        var ilGenerator = methodBuilder.GetILGenerator();

        // Log before the call
        ilGenerator.Emit(OpCodes.Ldarg_0); // Load 'this'
        ilGenerator.Emit(OpCodes.Ldfld, _interceptorField); // Load interceptor
        ilGenerator.Emit(OpCodes.Ldtoken, method);
        ilGenerator.Emit(OpCodes.Call, typeof(MethodBase).GetMethod(nameof(MethodBase.GetMethodFromHandle), new[] { typeof(RuntimeMethodHandle) }));
        ilGenerator.Emit(OpCodes.Castclass, typeof(MethodInfo));
        ilGenerator.Emit(OpCodes.Ldc_I4, parameterTypes.Length);
        ilGenerator.Emit(OpCodes.Newarr, typeof(object));

        DeclareParameters(parameterTypes, ilGenerator);

        ilGenerator.Emit(OpCodes.Callvirt, typeof(IInterceptor).GetMethod(nameof(IInterceptor.BeforeInvoke)));

        // Call the real method
        ilGenerator.Emit(OpCodes.Ldarg_0); // Load 'this'
        ilGenerator.Emit(OpCodes.Ldfld, _realObjectField); // Load real object

        for (int i = 0; i < parameterTypes.Length; i++)
        {
          ilGenerator.Emit(OpCodes.Ldarg, i + 1); // Load method argument
        }

        ilGenerator.Emit(OpCodes.Callvirt, method); // Invoke actual method

        // Log after the call
        ilGenerator.Emit(OpCodes.Ldarg_0); // Load 'this'
        ilGenerator.Emit(OpCodes.Ldfld, _interceptorField); // Load interceptor
        ilGenerator.Emit(OpCodes.Ldtoken, method);
        ilGenerator.Emit(OpCodes.Call, typeof(MethodBase).GetMethod(nameof(MethodBase.GetMethodFromHandle), new[] { typeof(RuntimeMethodHandle) }));
        ilGenerator.Emit(OpCodes.Castclass, typeof(MethodInfo));
        ilGenerator.Emit(OpCodes.Ldc_I4, parameterTypes.Length);
        ConvertArgsToArray(ilGenerator, parameterTypes);

        if (method.ReturnType == typeof(void))
        {
          ilGenerator.Emit(OpCodes.Ldnull);
        }
        else if (method.ReturnType.IsValueType)
        {
          ilGenerator.Emit(OpCodes.Box, method.ReturnType);
        }

        ilGenerator.Emit(OpCodes.Callvirt, typeof(IInterceptor).GetMethod(nameof(IInterceptor.AfterInvoke)));

        if (method.ReturnType == typeof(void))
        {
          ilGenerator.Emit(OpCodes.Ret);
        }
        else
        {
          ilGenerator.Emit(OpCodes.Unbox_Any, method.ReturnType);
          ilGenerator.Emit(OpCodes.Ret);
        }
      }
    }
    private static void ConvertArgsToArray(ILGenerator ilGenerator, Type[] parameterTypes)
    {

      ilGenerator.Emit(OpCodes.Newarr, typeof(object));

      for (int i = 0; i < parameterTypes.Length; i++)
      {
        ilGenerator.Emit(OpCodes.Dup);
        ilGenerator.Emit(OpCodes.Ldc_I4, i);
        ilGenerator.Emit(OpCodes.Ldarg, i + 1); // Load method argument
        if (parameterTypes[i].IsValueType)
        {
          ilGenerator.Emit(OpCodes.Box, parameterTypes[i]);
        }
        ilGenerator.Emit(OpCodes.Stelem_Ref);
      }
    }
    private static void DeclareParameters(Type[] parameterTypes, ILGenerator ilGenerator)
    {

      for (int i = 0; i < parameterTypes.Length; i++)
      {
        ilGenerator.Emit(OpCodes.Dup);
        ilGenerator.Emit(OpCodes.Ldc_I4, i);
        ilGenerator.Emit(OpCodes.Ldarg, i + 1); // Load method argument
        if (parameterTypes[i].IsValueType)
        {
          ilGenerator.Emit(OpCodes.Box, parameterTypes[i]);
        }
        ilGenerator.Emit(OpCodes.Stelem_Ref);
      }
    }
    public void GenerateConstructor()
    {
      /*
         ctor(obj, interceptor)
         {
           _obj = obj; 
           _interceptor = interceptor;
         }  
      */
      // Define constructor to initialize the fields
      var constructor = _typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, new[] { typeof(T), typeof(IInterceptor) });
      var ctorIL = constructor.GetILGenerator();
      ctorIL.Emit(OpCodes.Ldarg_0);
      ctorIL.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes));
      ctorIL.Emit(OpCodes.Ldarg_0);
      ctorIL.Emit(OpCodes.Ldarg_1);
      ctorIL.Emit(OpCodes.Stfld, _realObjectField);
      ctorIL.Emit(OpCodes.Ldarg_0);
      ctorIL.Emit(OpCodes.Ldarg_2);
      ctorIL.Emit(OpCodes.Stfld, _interceptorField);
      ctorIL.Emit(OpCodes.Ret);
    }

    public Type Build() => _typeBuilder.CreateType();
  }

  public static class ProxyGenerator
  {
    private static readonly AssemblyName _assemblyName;
    private static readonly AssemblyBuilder _assemblyBuilder;
    private static readonly ModuleBuilder _moduleBuilder;

    static ProxyGenerator()
    {
      _assemblyName = new AssemblyName("DynamicProxyAssembly");
      _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(_assemblyName, AssemblyBuilderAccess.Run);
      _moduleBuilder = _assemblyBuilder.DefineDynamicModule("DynamicProxyModule");
    }

    public static TInterface CreateProxy<TInterface>(TInterface realObject, IInterceptor interceptor) where TInterface : class
    {
      var typeBuilder = new ProxyTypeBuilder<TInterface>(_moduleBuilder);
      typeBuilder.GenerateMethods();
      typeBuilder.GenerateConstructor();
      Type proxyType = typeBuilder.Build();
      return (TInterface)Activator.CreateInstance(proxyType, realObject, interceptor);
    }
  }
}