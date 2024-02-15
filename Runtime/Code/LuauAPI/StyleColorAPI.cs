using System;
using UnityEngine;
using UnityEngine.UIElements;
using Luau;

[LuauAPI]
public class StyleColorAPI : BaseLuaAPIClass
{
    public override Type GetAPIType()
    {
        return typeof(UnityEngine.UIElements.StyleColor);
    }

    public override int OverrideMemberMethod(IntPtr thread, LuauSecurityContext securityContext, System.Object targetObject, string methodName, int numParameters, int[] parameterDataPODTypes, IntPtr[] parameterDataPtrs, int[] paramaterDataSizes)
    {
        if (methodName == "SetColor")
        {
            if (numParameters != 1)
            {
                ThreadDataManager.Error(thread);
                Debug.LogError("Error: SetColor takes 1 parameter");
                return 0;
            }
             
            Color col = LuauCore.GetParameterAsColor(0, numParameters, parameterDataPODTypes, parameterDataPtrs, paramaterDataSizes);
            StyleColor visual = (StyleColor)targetObject;
            visual.value = col;
            return 0;
        }
        return -1;
    }
}