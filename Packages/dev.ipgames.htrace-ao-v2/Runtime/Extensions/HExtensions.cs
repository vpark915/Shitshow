using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

#if UNITY_2022
using UnityEngine.Experimental.Rendering;  //using UnityEngine.Experimental.Rendering; for 2021 UNITY_2022
#endif

namespace IPGames.HTraceAO.Runtime.Extensions
{
	
	public static class HExtensions
	{
		public static void DebugPrint(Globals.DebugType type, string msg)
		{
			msg = "HTrace log: " + msg;
			
			switch (type)
			{
				case Globals.DebugType.Log:
					Debug.Log(msg);
					break;
				case Globals.DebugType.Warning:
					Debug.LogWarning(msg);
					break;
				case Globals.DebugType.Error:
					Debug.LogError(msg);
					break;
			}
		}
		
#if UNITY_EDITOR
		public static T Load<T>(string assetPath) where T : UnityEngine.Object
		{
			T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
			if (asset == null)
				Debug.LogError($"Missing {typeof(T).Name} at path '{assetPath}'.");

			return asset;
		}

		public static bool Load<T>(ref T field, string assetPath) where T : UnityEngine.Object
		{
			T asset = Load<T>(assetPath);
			if (asset == null || field == asset)
				return false;

			field = asset;
			return true;
		}
#endif

		public static bool ContainsOnOfElement(this string str, string[] elements)
		{
			foreach (var element in elements)
			{
				if (str.Contains(element))
					return true;
			}
			return false;
		}

		public static T NextEnum<T>(this T src) where T : struct
		{
			if (!typeof(T).IsEnum) throw new ArgumentException(String.Format("Argument {0} is not an Enum", typeof(T).FullName));

			T[] Arr = (T[])Enum.GetValues(src.GetType());
			int j = Array.IndexOf<T>(Arr, src) + 1;
			src = (Arr.Length == j) ? Arr[0] : Arr[j];
			return src;
		}
		
		
		public static Version GetUnityVersion()
		{
			return Version.Parse(GetNumericVersion(Application.unityVersion));
		}
		
		public static bool IsUnityLTS(string version)
		{
			// 2023.1.5f1
			if (!Version.TryParse(GetNumericVersion(version), out var current))
				return false;
            
			return
				current >= new Version(6000, 3, 0) && current < new Version(6000, 3, 100) ||
				current >= new Version(6000, 0, 23) && current < new Version(6000, 0, 100) ||
				current >= new Version(2022, 3, 0) && current < new Version(2022, 3, 100) ||
				current >= new Version(2023, 1, 0) && current < new Version(2023, 1, 100) ||
				current >= new Version(2023, 2, 0) && current < new Version(2023, 2, 100);
		}

		private static string GetNumericVersion(string version)
		{
			int index = version.IndexOfAny(new[] { 'f', 'a', 'b' });
			return index > 0 ? version.Substring(0, index) : version;
		}

		//custom Attributes
#if UNITY_EDITOR
		
		/// <summary>
		/// Read Only attribute.
		/// Attribute is use only to mark ReadOnly properties.
		/// </summary>
		public class ReadOnlyAttribute : PropertyAttribute
		{
		}

		/// <summary>
		/// This class contain custom drawer for ReadOnly attribute.
		/// </summary>
		[CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
		public class ReadOnlyDrawer : PropertyDrawer
		{
			/// <summary>
			/// Unity method for drawing GUI in Editor
			/// </summary>
			/// <param name="position">Position.</param>
			/// <param name="property">Property.</param>
			/// <param name="label">Label.</param>
			public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
			{
				// Saving previous GUI enabled value
				var previousGUIState = GUI.enabled;
				// Disabling edit for property
				GUI.enabled = false;
				// Drawing Property
				EditorGUI.PropertyField(position, property, label);
				// Setting old GUI enabled value
				GUI.enabled = previousGUIState;
			}
		}

		/// <summary>
		/// Filters enum values in Inspector based on a method that returns visibility for each value.
		/// The method must have signature: bool MethodName(EnumType value)
		/// </summary>
		[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
		public class FilteredEnumAttribute : PropertyAttribute
		{
			public string FilterMethodName { get; }

			public FilteredEnumAttribute(string filterMethodName)
			{
				FilterMethodName = filterMethodName;
			}
		}

		[CustomPropertyDrawer(typeof(FilteredEnumAttribute))]
		public class FilteredEnumDrawer : PropertyDrawer
		{
			public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
			{
				var attr = (FilteredEnumAttribute)attribute;

				// Get the object that contains this field
				object targetObject = GetParentObject(property);
				if (targetObject == null)
				{
					EditorGUI.PropertyField(position, property, label);
					return;
				}

				// Find the filter method
				var method = targetObject.GetType().GetMethod(attr.FilterMethodName,
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

				if (method == null)
				{
					Debug.LogWarning($"FilteredEnum: Method '{attr.FilterMethodName}' not found on {targetObject.GetType().Name}");
					EditorGUI.PropertyField(position, property, label);
					return;
				}

				// Collect visible enum values
				var enumType = fieldInfo.FieldType;
				var allValues = Enum.GetValues(enumType);
				var visibleValues = new System.Collections.Generic.List<int>();
				var visibleNames = new System.Collections.Generic.List<string>();

				foreach (var val in allValues)
				{
					bool shouldShow = (bool)method.Invoke(targetObject, new object[] { val });
					if (shouldShow)
					{
						visibleValues.Add((int)val);

						// Get display name from InspectorName attribute or fallback to nicified name
						var memberInfo = enumType.GetMember(val.ToString());
						var inspectorName = memberInfo.Length > 0
							? memberInfo[0].GetCustomAttribute<InspectorNameAttribute>()?.displayName
							: null;
						visibleNames.Add(inspectorName ?? ObjectNames.NicifyVariableName(val.ToString()));
					}
				}

				// Find current selection index
				int currentIndex = visibleValues.IndexOf(property.enumValueIndex);
				if (currentIndex < 0)
				{
					// Current value is hidden - reset to first visible
					currentIndex = 0;
					if (visibleValues.Count > 0)
						property.enumValueIndex = visibleValues[0];
				}

				EditorGUI.BeginProperty(position, label, property);
				int newIndex = EditorGUI.Popup(position, label.text, currentIndex, visibleNames.ToArray());
				if (newIndex >= 0 && newIndex < visibleValues.Count)
					property.enumValueIndex = visibleValues[newIndex];
				EditorGUI.EndProperty();
			}

			/// <summary>
			/// Gets the object instance that directly contains the serialized property field.
			/// Handles nested objects like RTAOSettings inside HTraceAO.
			/// </summary>
			private object GetParentObject(SerializedProperty property)
			{
				string path = property.propertyPath;
				object obj = property.serializedObject.targetObject;

				// Split path and traverse, stopping before the last element (the field itself)
				string[] elements = path.Replace(".Array.data[", "[").Split('.');

				for (int i = 0; i < elements.Length - 1; i++)
				{
					string element = elements[i];

					if (element.Contains("["))
					{
						// Array element
						string arrayName = element.Substring(0, element.IndexOf("["));
						int index = int.Parse(element.Substring(element.IndexOf("[")).Replace("[", "").Replace("]", ""));
						obj = GetFieldValue(obj, arrayName, index);
					}
					else
					{
						obj = GetFieldValue(obj, element);
					}

					if (obj == null) return null;
				}

				return obj;
			}

			private object GetFieldValue(object source, string name)
			{
				if (source == null) return null;

				var type = source.GetType();
				var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
				if (field != null) return field.GetValue(source);

				var prop = type.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
				return prop?.GetValue(source);
			}

			private object GetFieldValue(object source, string name, int index)
			{
				var enumerable = GetFieldValue(source, name) as System.Collections.IEnumerable;
				if (enumerable == null) return null;

				var enumerator = enumerable.GetEnumerator();
				for (int i = 0; i <= index; i++)
					if (!enumerator.MoveNext()) return null;

				return enumerator.Current;
			}
		}
#endif
		
		/// <summary>
		///   <para>Attribute used to make a float or int variable in a script be restricted to a specific range.</para>
		/// </summary>
		[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
		public class HRangeAttribute : Attribute
		{
			public readonly bool isFloat;

			public readonly float minFloat;
			public readonly float maxFloat;
			public readonly int minInt;
			public readonly int maxInt;

			/// <summary>
			///   <para>Attribute used to make a float or int variable in a script be restricted to a specific range.</para>
			/// </summary>
			/// <param name="minFloat">The minimum allowed value.</param>
			/// <param name="maxFloat">The maximum allowed value.</param>
			public HRangeAttribute(float minFloat, float maxFloat)
			{
				this.minFloat = minFloat;
				this.maxFloat = maxFloat;
				isFloat = true;
			}

			/// <summary>
			///   <para>Attribute used to make a float or int variable in a script be restricted to a specific range.</para>
			/// </summary>
			/// <param name="minFloat">The minimum allowed value.</param>
			/// <param name="maxFloat">The maximum allowed value.</param>
			public HRangeAttribute(int minInt, int maxInt)
			{
				this.minInt = minInt;
				this.maxInt = maxInt;
				isFloat = false;
			}
		}

		public struct HRangeAttributeElement
		{
			public bool isFloat;
			public float minFloat;
			public float maxFloat;
			public int minInt;
			public int maxInt;
		}

		public static float Clamp(float value, Type type, string nameOfField)
		{
			HRangeAttribute rangeAttribute = null;
			
			var filed = type.GetField(nameOfField);
			if (filed != null)
			{
				rangeAttribute = filed.GetCustomAttribute<HRangeAttribute>();
			}
			var property = type.GetProperty(nameOfField);
			if (property != null)
			{
				rangeAttribute = property.GetCustomAttribute<HRangeAttribute>();
			}

			return Mathf.Clamp(value, rangeAttribute.minFloat, rangeAttribute.maxFloat);
		}

		public static int Clamp(int value, Type type, string nameOfField)
		{
			HRangeAttribute rangeAttribute = null;
			
			var filed = type.GetField(nameOfField);
			if (filed != null)
			{
				rangeAttribute = filed.GetCustomAttribute<HRangeAttribute>();
			}
			var property = type.GetProperty(nameOfField);
			if (property != null)
			{
				rangeAttribute = property.GetCustomAttribute<HRangeAttribute>();
			}

			return Mathf.Clamp(value, rangeAttribute.minInt, rangeAttribute.maxInt);
		}
		
		public static void HRelease(this ComputeBuffer computeBuffer)
		{
			if (computeBuffer != null)
				computeBuffer.Release();
		}

		public static void HRelease(this CommandBuffer commandBuffer)
		{
			if (commandBuffer != null)
			{
				commandBuffer.Clear();
				commandBuffer.Release();
			}
		}

		public static void HRelease(this GraphicsBuffer graphicsBuffer)
		{
			if (graphicsBuffer != null)
			{
				graphicsBuffer.Release();
			}
		}

		public static void HRelease(this Wrappers.HDynamicBuffer hDynamicBuffer)
		{
			if (hDynamicBuffer != null)
			{
				hDynamicBuffer.Release();
			}
		}

		public static void HRelease(this RayTracingAccelerationStructure rayTracingAccelerationStructure)
		{
			if (rayTracingAccelerationStructure != null)
			{
				rayTracingAccelerationStructure.Release();
			}
		}
	}
}
