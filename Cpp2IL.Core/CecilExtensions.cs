using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Cpp2IL.Core.Utils;
using Mono.Cecil;

namespace Cpp2IL.Core
{
    internal static class CecilExtensions
    {
        internal static readonly ConcurrentDictionary<TypeDefinition, ConcurrentDictionary<TypeReference, bool>> AssignabilityCache = new();

        /// <summary>
        /// Is childTypeDef a subclass of parentTypeDef. Does not test interface inheritance
        /// </summary>
        /// <param name="childTypeDef"></param>
        /// <param name="parentTypeDef"></param>
        /// <returns></returns>
        public static bool IsSubclassOf(this TypeReference childTypeDef, TypeReference parentTypeDef) =>
            childTypeDef.MetadataToken
            != parentTypeDef.MetadataToken
            && childTypeDef
                .EnumerateBaseClasses()
                .Any(b => b.MetadataToken == parentTypeDef.MetadataToken);

        /// <summary>
        /// Does childType inherit from parentInterface
        /// </summary>
        /// <param name="childType"></param>
        /// <param name="parentInterfaceDef"></param>
        /// <returns></returns>
        public static bool DoesAnySuperTypeImplementInterface(this TypeReference childType, TypeReference parentInterfaceDef)
        {
            return childType
                .EnumerateBaseClasses()
                .Any(typeDefinition => typeDefinition.DoesSpecificTypeImplementInterface(parentInterfaceDef));
        }

        /// <summary>
        /// Does the childType directly inherit from parentInterface. Base
        /// classes of childType are not tested
        /// </summary>
        /// <param name="childTypeDef"></param>
        /// <param name="parentInterfaceDef"></param>
        /// <returns></returns>
        public static bool DoesSpecificTypeImplementInterface(this TypeReference childTypeDef, TypeReference parentInterfaceDef)
        {
            return childTypeDef
                .Resolve()?
                .Interfaces
                .Any(ifaceDef => DoesSpecificInterfaceImplementInterface(ifaceDef.InterfaceType.Resolve(), parentInterfaceDef)) ?? false;
        }

        /// <summary>
        /// Does interface iface0 equal or implement interface iface1
        /// </summary>
        /// <param name="iface0"></param>
        /// <param name="iface1"></param>
        /// <returns></returns>
        public static bool DoesSpecificInterfaceImplementInterface(TypeReference iface0, TypeReference iface1)
        {
            return iface0.MetadataToken == iface1.MetadataToken || iface0.DoesAnySuperTypeImplementInterface(iface1);
        }

        /// <summary>
        /// Is source type assignable to target type
        /// </summary>
        /// <param name="instanceOrBaseClass"></param>
        /// <param name="potentialSubclass"></param>
        /// <returns></returns>
        public static bool IsAssignableFrom(this TypeDefinition? instanceOrBaseClass, TypeReference? potentialSubclass)
        {
            //Do the quick checks first, they don't need to be cached.
            if (instanceOrBaseClass is null || potentialSubclass is null)
                return false;

            if (instanceOrBaseClass == potentialSubclass)
                return true;

            if (instanceOrBaseClass.MetadataToken == potentialSubclass.Resolve()?.MetadataToken)
                return true;

            //Slow checks are cached
            if (!AssignabilityCache.TryGetValue(instanceOrBaseClass, out var subclassCache))
            {
                subclassCache = new();
                AssignabilityCache.TryAdd(instanceOrBaseClass, subclassCache);
            }

            if (subclassCache.TryGetValue(potentialSubclass, out var ret))
                return ret;
            
            ret = potentialSubclass.IsSubclassOf(instanceOrBaseClass)
                   || instanceOrBaseClass.IsInterface && potentialSubclass.DoesAnySuperTypeImplementInterface(instanceOrBaseClass)
                   || instanceOrBaseClass.IsEnumerableLikeAndSoIs(potentialSubclass);

            subclassCache.TryAdd(potentialSubclass, ret);

            return ret;
        }

        /// <summary>
        /// Enumerate the current type, it's parent and all the way to the top type
        /// </summary>
        /// <param name="klassType"></param>
        /// <returns></returns>
        public static IEnumerable<TypeReference> EnumerateBaseClasses(this TypeReference klassType)
        {
            for (var typeDefinition = klassType; typeDefinition != null; typeDefinition = typeDefinition.Resolve()?.BaseType?.Resolve())
            {
                yield return typeDefinition;
            }
        }

        public static bool IsEnumerableLikeAndSoIs(this TypeDefinition reference, TypeReference otherType)
        {
            //-able
            if (reference.FullName.StartsWith("System.Collections.Generic.IEnumerable"))
            {
                return MiscUtils.IEnumerableReference.IsAssignableFrom(otherType);
            }

            return false;
        }
    }
}