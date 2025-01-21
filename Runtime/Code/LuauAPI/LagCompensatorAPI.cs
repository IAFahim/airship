using System;
using Mirror;
using UnityEngine;

[LuauAPI]
public class LagCompensatorAPI : BaseLuaAPIClass {
    public override Type GetAPIType() {
        return typeof(LagCompensator);
    }

    public override int OverrideMemberMethod(LuauContext context, IntPtr thread, object targetObject, string methodName, int numParameters,
        ArraySegment<int> parameterDataPODTypes, ArraySegment<IntPtr> parameterDataPtrs, ArraySegment<int> parameterDataSizes) {

        LagCompensator target = (LagCompensator)targetObject;
        if (methodName == "RaycastCheck") {
            NetworkConnectionToClient viewer = (NetworkConnectionToClient) LuauCore.GetParameterAsObject(0, numParameters, parameterDataPODTypes,
                parameterDataPtrs, parameterDataSizes, thread);
            Vector3 originPoint = LuauCore.GetParameterAsVector3(1, numParameters, parameterDataPODTypes,
                parameterDataPtrs, parameterDataSizes);
            Vector3 hitPoint = LuauCore.GetParameterAsVector3(2, numParameters, parameterDataPODTypes,
                parameterDataPtrs, parameterDataSizes);

            float tolerancePercent = 0;
            if (numParameters >= 4) {
                tolerancePercent = LuauCore.GetParameterAsFloat(3, numParameters, parameterDataPODTypes,
                    parameterDataPtrs, parameterDataSizes);
            }

            int layerMask = -1;
            if (numParameters >= 5) {
                layerMask = LuauCore.GetParameterAsInt(4, numParameters, parameterDataPODTypes,
                    parameterDataPtrs, parameterDataSizes);
            }

            bool result = target.RaycastCheck(viewer, originPoint, hitPoint, tolerancePercent, layerMask,
                out RaycastHit hit);
            if (result) {
                LuauCore.WritePropertyToThread(thread, hit, hit.GetType());
            } else {
                LuauCore.WritePropertyToThread(thread, null, null);
            }
            
            return 1;
        }

        return -1;
    }
}
