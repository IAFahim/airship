
using System;
using Luau;
using UnityEngine;

//Simplified one, don't use this as an example!
public class UnityCustomAPI : BaseLuaAPIClass
{
    Type type = null;
 
    public UnityCustomAPI(Type theType)
    {
        type = theType;
    }
    public override Type GetAPIType()
    {
        return type;
    }

    public override int OverrideStaticMethod(IntPtr thread, LuauSecurityContext securityContext, string methodName, int numParameters, int[] parameterDataPODTypes, IntPtr[] parameterDataPtrs, int[] paramaterDataSizes)
    {
        //Shouldn't ever have to implement anything here for your own custom c# types!
        return -1;
    }

    public override int OverrideMemberMethod(IntPtr thread, LuauSecurityContext securityContext, System.Object targetObject, string methodName, int numParameters, int[] parameterDataPODTypes, IntPtr[] parameterDataPtrs, int[] paramaterDataSizes)
    {
        //Shouldn't ever have to implement anything here for your own custom c# types!
        return -1;
    }
}