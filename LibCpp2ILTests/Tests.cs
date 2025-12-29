using System;
using System.Net;
using LibCpp2IL;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;
using Xunit.Abstractions;

namespace LibCpp2ILTests
{
    public class Tests
    {
        private readonly ITestOutputHelper _outputHelper;

        public Tests(ITestOutputHelper outputHelper)
        {
            _outputHelper = outputHelper;
        }

        private static WebClient client = new();
        private void CheckFiles(string metadataUrl, string binaryUrl, int[] unityVer)
        {
            _outputHelper.WriteLine($"Downloading: {metadataUrl}...");
            var metadataBytes = client.DownloadData(metadataUrl);
            _outputHelper.WriteLine($"Got {metadataBytes.Length / 1024 / 1024} MB metadata file.");
            
            _outputHelper.WriteLine($"Downloading: {binaryUrl}...");
            var binaryBytes = client.DownloadData(binaryUrl);
            _outputHelper.WriteLine($"Got {binaryBytes.Length / 1024 / 1024} MB binary file.");

            //Configure the lib.
            LibCpp2IlMain.Settings.DisableGlobalResolving = true;
            LibCpp2IlMain.Settings.DisableMethodPointerMapping = true;
            LibCpp2IlMain.Settings.AllowManualMetadataAndCodeRegInput = false;

            //Clean up any previous runs
            LibCpp2IlMain.TheMetadata = null;
            LibCpp2IlMain.Binary = null;

            _outputHelper.WriteLine("Invoking LibCpp2IL...");
            Assert.True(LibCpp2IlMain.Initialize(binaryBytes, metadataBytes, unityVer));
            _outputHelper.WriteLine("Done.");
        }
        
        [Fact]
        public void Metadata24_1_64BitSupportIsPresent() => CheckFiles("http://samboycoding.me/static/meta_24.1_x64.dat", "http://samboycoding.me/static/GA_24.1_x64.dll", new[] {2018, 4, 20});
        
        [Fact]
        public void Metadata24_3_32BitSupportIsPresent() => CheckFiles("http://samboycoding.me/static/meta_24.3_x64.dat", "http://samboycoding.me/static/GA_24.3_x64.dll", new[] {2019, 4, 11});

        [Fact]
        public void Metadata24_3_ARM32ElfSupportIsPresent() => CheckFiles("http://samboycoding.me/static/meta_24.3_arm32.dat", "http://samboycoding.me/static/GA_24.3_arm32.so", new[] {2019, 4, 20});

        [Fact]
        public void Metadata27_1_32BitSupportIsPresent() => CheckFiles("http://samboycoding.me/static/meta_27.1_x32.dat", "http://samboycoding.me/static/GA_27.1_x32.dll", new[] {2020, 2, 6});

        [Fact]
        public void Metadata27_1_AARCH64ElfSupportIsPresent() => CheckFiles("http://samboycoding.me/static/meta_27.1_aarch64.dat", "http://samboycoding.me/static/GA_27.1_aarch64.so", new[] {2020, 2, 6});

        [Fact]
        public void WritingAssemblyWithGenericInstanceArraySucceeds()
        {
            // Create a "UnityEngine" module with GameObject and Vector3 types
            var unityModule = ModuleDefinition.CreateModule("UnityEngine", ModuleKind.Dll);
            var gameObject = new TypeDefinition("UnityEngine", "GameObject", TypeAttributes.Public | TypeAttributes.Class, unityModule.ImportReference(typeof(object)));
            var vector3 = new TypeDefinition("UnityEngine", "Vector3", TypeAttributes.Public | TypeAttributes.Class, unityModule.ImportReference(typeof(object)));
            unityModule.Types.Add(gameObject);
            unityModule.Types.Add(vector3);

            // Create a second module with a generic Tuple`2 type and a method that does newarr Tuple`2<GameObject,Vector3>
            var mod = ModuleDefinition.CreateModule("TestAssembly", ModuleKind.Dll);
            // Add assembly reference to UnityEngine
            var unityRef = new AssemblyNameReference("UnityEngine", new Version(1,0,0,0));
            mod.AssemblyReferences.Add(unityRef);

            // Create Tuple`2<T1,T2>
            var tupleType = new TypeDefinition("Jackpot", "Tuple`2", TypeAttributes.Public | TypeAttributes.Class, mod.ImportReference(typeof(object)));
            tupleType.GenericParameters.Add(new GenericParameter("T1", tupleType));
            tupleType.GenericParameters.Add(new GenericParameter("T2", tupleType));
            mod.Types.Add(tupleType);

            // Create a method
            var typeForMethod = new TypeDefinition("Jackpot", "User", TypeAttributes.Public | TypeAttributes.Class, mod.ImportReference(typeof(object)));
            mod.Types.Add(typeForMethod);
            var method = new MethodDefinition("Test", MethodAttributes.Public | MethodAttributes.Static, mod.ImportReference(typeof(void)));
            typeForMethod.Methods.Add(method);

            var il = method.Body.GetILProcessor();
            // Import references to UnityEngine types via a TypeReference pointing to the external assembly
            var goRef = new TypeReference("UnityEngine", "GameObject", mod, unityRef);
            var vecRef = new TypeReference("UnityEngine", "Vector3", mod, unityRef);

            // Construct generic instance Tuple`2<GameObject,Vector3>
            var tupleRef = new GenericInstanceType(mod.ImportReference(tupleType));
            tupleRef.GenericArguments.Add(goRef);
            tupleRef.GenericArguments.Add(vecRef);

            // Emit instructions: ldc.i4.1, newarr tupleRef, pop, ret
            il.Emit(OpCodes.Ldc_I4_1);
            // IMPORTANT: using the same path as the runtime will; this used to fail during write if generics were not imported recursively
            il.Emit(OpCodes.Newarr, mod.ImportReference(tupleRef));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ret);

            // Now attempt to write the module to a MemoryStream - this must not throw
            using (var ms = new System.IO.MemoryStream())
            {
                mod.Write(ms);
            }
        }
    }
}