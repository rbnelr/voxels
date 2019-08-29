using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

public static class NoAllocHelpers {
	private static readonly Dictionary<Type, Delegate> ExtractArrayFromListTDelegates = new Dictionary<Type, Delegate>();
	private static readonly Dictionary<Type, Delegate> ResizeListDelegates = new Dictionary<Type, Delegate>();

	/// <summary>
	/// Extract the internal array from a list.
	/// </summary>
	/// <typeparam name="T"><see cref="List{T}"/>.</typeparam>
	/// <param name="list">The <see cref="List{T}"/> to extract from.</param>
	/// <returns>The internal array of the list.</returns>
	public static T[] ExtractArrayFromListT<T> (List<T> list) {
		if (!ExtractArrayFromListTDelegates.TryGetValue(typeof(T), out var obj)) {
			var ass = Assembly.GetAssembly(typeof(Mesh)); // any class in UnityEngine
			var type = ass.GetType("UnityEngine.NoAllocHelpers");
			var methodInfo = type.GetMethod("ExtractArrayFromListT", BindingFlags.Static | BindingFlags.Public)
				.MakeGenericMethod(typeof(T));

			obj = ExtractArrayFromListTDelegates[typeof(T)] = Delegate.CreateDelegate(typeof(Func<List<T>, T[]>), methodInfo);
		}

		var func = (Func<List<T>, T[]>)obj;
		return func.Invoke(list);
	}

	/// <summary>
	/// Resize a list.
	/// </summary>
	/// <typeparam name="T"><see cref="List{T}"/>.</typeparam>
	/// <param name="list">The <see cref="List{T}"/> to resize.</param>
	/// <param name="size">The new length of the <see cref="List{T}"/>.</param>
	public static void ResizeList<T> (List<T> list, int size) {
		if (!ResizeListDelegates.TryGetValue(typeof(T), out var obj)) {
			var ass = Assembly.GetAssembly(typeof(Mesh)); // any class in UnityEngine
			var type = ass.GetType("UnityEngine.NoAllocHelpers");
			var methodInfo = type.GetMethod("ResizeList", BindingFlags.Static | BindingFlags.Public)
				.MakeGenericMethod(typeof(T));
			obj = ResizeListDelegates[typeof(T)] =
				Delegate.CreateDelegate(typeof(Action<List<T>, int>), methodInfo);
		}

		var action = (Action<List<T>, int>)obj;
		action.Invoke(list, size);
	}
}

public static class MeshExt {
	
	// https://forum.unity.com/threads/nativearray-and-mesh.522951/
	// avoid having to call NativeList.ToArray() when assigning a Mesh attribute which results in garbage
	//  There seems some GCAllocs still happen, but CPU spikes seem to be improved alot
	// NOTE: that the buffer resizes up to the size of native, and does not shrink to avoid allocations -> Potential Memory Hog
	// TODO: This HACK will go away with the official support of NativeArrays / NativeLists? in https://forum.unity.com/threads/feedback-wanted-mesh-scripting-api-improvements.684670/
	static unsafe void assignNativeListToBuffer<TNative, T> (NativeList<TNative> native, ref List<T> buffer) where TNative : struct where T : struct {
		//Debug.Assert(buffer.Count == 0);
		Debug.Assert(UnsafeUtility.SizeOf<TNative>() == UnsafeUtility.SizeOf<T>());
		
		if (native.Length > 0) {
			if (buffer.Capacity < native.Length) {
				buffer.Capacity = native.Length;
			}

			var arr = NoAllocHelpers.ExtractArrayFromListT(buffer);
			var size = UnsafeUtility.SizeOf<T>();
		
			var ptr = (byte*)UnsafeUtility.AddressOf(ref arr[0]);
		
			UnsafeUtility.MemCpy(ptr, native.GetUnsafePtr(), native.Length * (long)size);
		}
		NoAllocHelpers.ResizeList(buffer, native.Length);
	}

	public static unsafe void SetVerticesNative (this Mesh mesh, NativeList<float3> vertices, ref List<Vector3> buffer) {
		assignNativeListToBuffer(vertices, ref buffer);
		mesh.SetVertices(buffer);
	}
	public static unsafe void SetNormalsNative (this Mesh mesh, NativeList<float3> normals, ref List<Vector3> buffer) {
		assignNativeListToBuffer(normals, ref buffer);
		mesh.SetNormals(buffer);
	}
	public static unsafe void SetUvsNative (this Mesh mesh, int channel, NativeList<float2> uvs, ref List<Vector2> buffer) {
		assignNativeListToBuffer(uvs, ref buffer);
		mesh.SetUVs(0, buffer);
	}
	public static unsafe void SetUvsNative (this Mesh mesh, int channel, NativeList<float4> uvs, ref List<Vector4> buffer) {
		assignNativeListToBuffer(uvs, ref buffer);
		mesh.SetUVs(0, buffer);
	}
	public static unsafe void SetColorsNative (this Mesh mesh, NativeList<Color> colors, ref List<Color> buffer) {
		assignNativeListToBuffer(colors, ref buffer);
		mesh.SetColors(buffer);
	}
	public static unsafe void SetTrianglesNative (this Mesh mesh, NativeList<int> triangles, int submesh, ref List<int> buffer) {
		assignNativeListToBuffer(triangles, ref buffer);
		mesh.SetTriangles(buffer, submesh);
	}
	
	public static unsafe void SetFloatArrayNative (this MaterialPropertyBlock MPB, string name, NativeList<float> data, ref List<float> buffer) {
		if (data.Length > 0) { // SetFloatArray with length 0 not allowed
			assignNativeListToBuffer(data, ref buffer);
			MPB.SetFloatArray(name, buffer);
		}
	}
}
