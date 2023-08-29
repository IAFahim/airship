#define DO_THREAD_SAFTEYCHECK
// #define DO_CALL_SAFTEYCHECK
using System;
using System.Runtime.InteropServices;
using UnityEngine;
using System.Threading;
using System.Runtime.InteropServices.WindowsRuntime;

public static class LuauPlugin
{
	public delegate void PrintCallback(IntPtr thread, int style, IntPtr buffer, int length);
	public delegate int GetPropertyCallback(IntPtr thread, int instanceId, IntPtr classNamePtr, int classNameSize, IntPtr propertyName, int propertyNameSize);
	public delegate int SetPropertyCallback(IntPtr thread, int instanceId, IntPtr classNamePtr, int classNameSize, IntPtr propertyName, int propertyNameSize, LuauCore.PODTYPE type, IntPtr propertyData, int propertySize);
	public delegate int CallMethodCallback(IntPtr thread, int instanceId, IntPtr className, int classNameSize, IntPtr methodName, int methodNameSize, int numParameters, IntPtr firstParameterType, IntPtr firstParameterData, IntPtr firstParameterSize, IntPtr shouldYield, IntPtr objPtr);
	public delegate int ObjectGCCallback(int instanceId, IntPtr objPointer);
	public delegate IntPtr RequireCallback(IntPtr thread, IntPtr fileName, int fileNameSize);
	public delegate int RequirePathCallback(IntPtr thread, IntPtr fileName, int fileNameSize);
	public delegate int YieldCallback(IntPtr thread, IntPtr host);

	public static Thread s_unityMainThread = null;
	public static bool s_currentlyExecuting = false;
	public enum CurrentCaller
	{
		None,
		RunThread,
		CallMethodOnThread,
		CreateThread
	}
	
    public static CurrentCaller s_currentCaller = CurrentCaller.None;


    public static void ThreadSafteyCheck()
	{
#if DO_THREAD_SAFTEYCHECK
		if (s_unityMainThread == null)
        {
			//Make the assumption that the first thread to call in here is the main thread
            s_unityMainThread = Thread.CurrentThread;
        }
        else
        {
            if (s_unityMainThread != Thread.CurrentThread)
            {
                Debug.LogError("LuauPlugin called from a thread other than the main thread!");
            }
        }
#endif       
    }

	public static void BeginExecutionCheck(CurrentCaller caller)
	{
#if DO_CALL_SAFTEYCHECK
		if (s_currentlyExecuting == true)
		{
            Debug.LogError("LuauPlugin called " + caller + " while a lua thread was still executing " + s_currentCaller);
        }
        s_currentCaller = caller;
		s_currentlyExecuting = true;
#endif
	}
    public static void EndExecutionCheck()
    {
#if DO_CALL_SAFTEYCHECK
        s_currentlyExecuting = false;
		s_currentCaller = CurrentCaller.None;
#endif
    }

#if UNITY_IPHONE
    [DllImport("__Internal")]
#else
    [DllImport("LuauPlugin", CallingConvention = CallingConvention.Cdecl)]
#endif
    private static extern bool InitializePrintCallback(PrintCallback printCallback);
    public static bool LuauInitializePrintCallback(PrintCallback printCallback)
    {
	    ThreadSafteyCheck();

	    bool returnValue = InitializePrintCallback(printCallback);
	    return returnValue;
    }

#if UNITY_IPHONE
    [DllImport("__Internal")]
#else
    [DllImport("LuauPlugin", CallingConvention = CallingConvention.Cdecl)]
#endif
	private static extern bool Startup(GetPropertyCallback getPropertyCallback, SetPropertyCallback setPropertyCallback, CallMethodCallback callMethodCallback, ObjectGCCallback gcCallback, RequireCallback requireCallback, IntPtr stringArray, int stringCount, RequirePathCallback requirePathCallback, YieldCallback yieldCallback);
	public static bool LuauStartup(GetPropertyCallback getPropertyCallback, SetPropertyCallback setPropertyCallback, CallMethodCallback callMethodCallback, ObjectGCCallback gcCallback, RequireCallback requireCallback, IntPtr stringArray, int stringCount, RequirePathCallback requirePathCallback, YieldCallback yieldCallback)
	{
        ThreadSafteyCheck();
        
        bool returnValue = Startup(getPropertyCallback, setPropertyCallback, callMethodCallback, gcCallback, requireCallback, stringArray, stringCount, requirePathCallback, yieldCallback);
        return returnValue;
    }

#if UNITY_IPHONE
    [DllImport("__Internal")]
#else
	[DllImport("LuauPlugin", CallingConvention = CallingConvention.Cdecl)]
#endif
	private static extern bool Reset();
	public static bool LuauReset()
	{
        ThreadSafteyCheck();

        s_unityMainThread = null;
        bool returnValue = Reset();
        return returnValue;
	}


#if UNITY_IPHONE
    [DllImport("__Internal")]
#else
	[DllImport("LuauPlugin", CallingConvention = CallingConvention.Cdecl)]
#endif
	private static extern bool Shutdown();
	public static bool LuauShutdown()
	{
		ThreadSafteyCheck();
 
        bool returnValue = Shutdown();
		return returnValue;
	}

#if UNITY_IPHONE
    [DllImport("__Internal")]
#else
	[DllImport("LuauPlugin")]
#endif
	private static extern IntPtr CreateThread(IntPtr script, int scriptLength, IntPtr filename, int filenameLength, int gameObjectId, bool binary);
	public static IntPtr LuauCreateThread(IntPtr script, int scriptLength, IntPtr filename, int filenameLength, int gameObjectId, bool binary)
	{
		ThreadSafteyCheck();
		BeginExecutionCheck(CurrentCaller.CreateThread);
		IntPtr returnValue = CreateThread(script, scriptLength, filename, filenameLength, gameObjectId, binary);
        EndExecutionCheck();
        return returnValue;
    }


#if UNITY_IPHONE
    [DllImport("__Internal")]
#else
	[DllImport("LuauPlugin")]
#endif
	private static extern IntPtr CompileCode(IntPtr script, int scriptLength, IntPtr filename, int filenameLength, int optimizationLevel);
	public static IntPtr LuauCompileCode(IntPtr script, int scriptLength, IntPtr filename, int filenameLength, int optimizationLevel)
	{
        ThreadSafteyCheck();
        IntPtr returnValue = CompileCode(script, scriptLength, filename, filenameLength, optimizationLevel);
		return returnValue;
	}

#if UNITY_IPHONE
    [DllImport("__Internal")]
#else
	[DllImport("LuauPlugin")]
#endif
	private static extern int RunThread(IntPtr thread, int nArgs);
	public static int LuauRunThread(IntPtr thread, int nArgs = 0)
	{
        ThreadSafteyCheck();
		//BeginExecutionCheck(CurrentCaller.CreateThread);
        int returnValue = RunThread(thread, nArgs);
        //EndExecutionCheck();
        return returnValue;
    }

#if UNITY_IPHONE
    [DllImport("__Internal")]
#else
	[DllImport("LuauPlugin")]
#endif
	private static extern int CallMethodOnThread(IntPtr thread, IntPtr methodName, int methodNameSize, int numParameters);
	public static int LuauCallMethodOnThread(IntPtr thread, IntPtr methodName, int methodNameSize, int numParameters)
	{
        ThreadSafteyCheck();
		BeginExecutionCheck(CurrentCaller.CallMethodOnThread);
        int returnValue = CallMethodOnThread(thread, methodName, methodNameSize, numParameters);
        EndExecutionCheck();
        return returnValue;
    }

#if UNITY_IPHONE
    [DllImport("__Internal")]
#else
	[DllImport("LuauPlugin")]
#endif
	private static extern void DestroyThread(IntPtr thread);
	public static void LuauDestroyThread(IntPtr thread)
	{
		Debug.Log("Destroying thread " + thread);
        ThreadSafteyCheck();
        DestroyThread(thread);
	}

#if UNITY_IPHONE
    [DllImport("__Internal")]
#else
	[DllImport("LuauPlugin")]
#endif
	private static extern void PinThread(IntPtr thread);
	public static void LuauPinThread(IntPtr thread)
	{
		// Debug.Log("Unpinning thread " + thread);
		ThreadSafteyCheck();
		PinThread(thread);
	}

#if UNITY_IPHONE
    [DllImport("__Internal")]
#else
	[DllImport("LuauPlugin")]
#endif
	private static extern void UnpinThread(IntPtr thread);
	public static void LuauUnpinThread(IntPtr thread)
	{
        // Debug.Log("Unpinning thread " + thread);
        ThreadSafteyCheck();
        UnpinThread(thread);
	}

#if UNITY_IPHONE
    [DllImport("__Internal")]
#else
	[DllImport("LuauPlugin")]
#endif
	private static extern void PushValueToThread(IntPtr thread, int type, IntPtr data, int dataSize);
	public static void LuauPushValueToThread(IntPtr thread, int type, IntPtr data, int dataSize)
	{
        ThreadSafteyCheck();
        PushValueToThread(thread, type, data, dataSize);
	}

#if UNITY_IPHONE
    [DllImport("__Internal")]
#else
	[DllImport("LuauPlugin")]
#endif
	private static extern void PushVector3ToThread(IntPtr thread, float x, float y, float z);
	public static void LuauPushVector3ToThread(IntPtr thread, float x, float y, float z)
	{
        ThreadSafteyCheck();
        PushVector3ToThread(thread, x, y, z);
	}
 

#if UNITY_IPHONE
    [DllImport("__Internal")]
#else
	[DllImport("LuauPlugin")]
#endif
	private static extern void GetDebugTrace(IntPtr thread);
	public static void LuauGetDebugTrace(IntPtr thread)
	{
        ThreadSafteyCheck();
        GetDebugTrace(thread);
	}

}