using System;
using Luau;
using UnityEngine;

[LuauAPI]
public class ParticleSystemAPI : BaseLuaAPIClass
{
    public override Type GetAPIType()
    {
        return typeof(ParticleSystem);
    }

    public override int OverrideStaticMethod(IntPtr thread, LuauSecurityContext securityContext, string methodName, int numParameters, int[] parameterDataPODTypes,
        IntPtr[] parameterDataPtrs, int[] paramaterDataSizes)
    {
        if (methodName == "MakeEmitParams")
        {
            var emitParams = new ParticleSystem.EmitParams();
            LuauCore.WritePropertyToThread(thread, emitParams, typeof(ParticleSystem.EmitParams));
            return 1;
        }

        return -1;
    }

    public override int OverrideMemberMethod(IntPtr thread, LuauSecurityContext securityContext, object targetObject, string methodName, int numParameters,
        int[] parameterDataPODTypes, IntPtr[] parameterDataPtrs, int[] paramaterDataSizes)
    {
        
        if (methodName == "EmitAtPosition")
        {
            if (numParameters == 2)
            {
                int amount = LuauCore.GetParameterAsInt(0, numParameters, parameterDataPODTypes, parameterDataPtrs,
                    paramaterDataSizes);
                Vector3 pos = LuauCore.GetParameterAsVector3(1, numParameters, parameterDataPODTypes, parameterDataPtrs,
                    paramaterDataSizes);

                var emitParams = new ParticleSystem.EmitParams();
                emitParams.position = pos;
                emitParams.applyShapeToPosition = true;

                Debug.Log("Emitting!");
                var system = (ParticleSystem)targetObject;
                system.Emit(emitParams, amount);

                return 0;
            }
        }
        
        return -1;
    }
}