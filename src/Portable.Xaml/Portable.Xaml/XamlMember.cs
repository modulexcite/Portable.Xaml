//
// Copyright (C) 2010 Novell Inc. http://novell.com
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
using System;
using System.Collections.Generic;
using Portable.Xaml.ComponentModel;
using System.Reflection;
using Portable.Xaml.Markup;
using Portable.Xaml.Schema;
using System.Linq;
using System.ComponentModel;

namespace Portable.Xaml
{
	public class XamlMember : IEquatable<XamlMember>
	{
		FlagValue flags;
		[Flags]
		enum MemberFlags {
			IsAmbient = 1 << 0,
			IsEvent = 1 << 1,
			IsReadOnly = 1 << 2,
			IsReadPublic = 1 << 3,
			IsUnknown = 1 << 4,
			IsWriteOnly = 1 << 5,
			IsWritePublic = 1 << 6,
			IsNameValid = 1 << 7,
			IsConstructorArgument = 1 << 8
		}
		XamlType target_type;
		MemberInfo underlying_member;
		MethodInfo underlying_getter, underlying_setter;
		XamlSchemaContext context;
		XamlMemberInvoker invoker;
		bool is_attachable, is_event, is_directive;
		bool is_predefined_directive = XamlLanguage.InitializingDirectives;
		string directive_ns;
		ReferenceValue<XamlType> targetType;
		ReferenceValue<XamlType> type;
		ReferenceValue<XamlValueConverter<TypeConverter>> typeConverter;
		ReferenceValue<XamlValueConverter<ValueSerializer>> valueSerializer;
		ReferenceValue<ICustomAttributeProvider> customAttributeProvider;
		ReferenceValue<XamlValueConverter<XamlDeferringLoader>> deferringLoader;

		public XamlMember (EventInfo eventInfo, XamlSchemaContext schemaContext)
			: this (eventInfo, schemaContext, null)
		{
		}

		public XamlMember (EventInfo eventInfo, XamlSchemaContext schemaContext, XamlMemberInvoker invoker)
			: this (schemaContext, invoker)
		{
			if (eventInfo == null)
				throw new ArgumentNullException ("eventInfo");
			Name = eventInfo.Name;
			underlying_member = eventInfo;
			DeclaringType = schemaContext.GetXamlType (eventInfo.DeclaringType);
			target_type = DeclaringType;
			UnderlyingSetter = eventInfo.GetAddMethod();
			is_event = true;
		}

		public XamlMember (PropertyInfo propertyInfo, XamlSchemaContext schemaContext)
			: this (propertyInfo, schemaContext, null)
		{
		}


		public XamlMember (PropertyInfo propertyInfo, XamlSchemaContext schemaContext, XamlMemberInvoker invoker)
			: this (schemaContext, invoker)
		{
			if (propertyInfo == null)
				throw new ArgumentNullException ("propertyInfo");
			Name = propertyInfo.Name;
			underlying_member = propertyInfo;
			DeclaringType = schemaContext.GetXamlType (propertyInfo.DeclaringType);
			target_type = DeclaringType;
			UnderlyingGetter = propertyInfo.GetPrivateGetMethod();
			UnderlyingSetter = propertyInfo.GetPrivateSetMethod();
		}

		public XamlMember (string attachableEventName, MethodInfo adder, XamlSchemaContext schemaContext)
			: this (attachableEventName, adder, schemaContext, null)
		{
		}

		public XamlMember (string attachableEventName, MethodInfo adder, XamlSchemaContext schemaContext, XamlMemberInvoker invoker)
			: this (schemaContext, invoker)
		{
			if (attachableEventName == null)
				throw new ArgumentNullException ("attachableEventName");
			if (adder == null)
				throw new ArgumentNullException ("adder");
			Name = attachableEventName;
			VerifyAdderSetter (adder);
			underlying_member = adder;
			DeclaringType = schemaContext.GetXamlType (adder.DeclaringType);
			target_type = schemaContext.GetXamlType (typeof (object));
			UnderlyingSetter = adder;
			is_event = true;
			is_attachable = true;
		}

		public XamlMember (string attachablePropertyName, MethodInfo getter, MethodInfo setter, XamlSchemaContext schemaContext)
			: this (attachablePropertyName, getter, setter, schemaContext, null)
		{
		}

		public XamlMember (string attachablePropertyName, MethodInfo getter, MethodInfo setter, XamlSchemaContext schemaContext, XamlMemberInvoker invoker)
			: this (schemaContext, invoker)
		{
			if (attachablePropertyName == null)
				throw new ArgumentNullException ("attachablePropertyName");
			if (getter == null && setter == null)
				throw new ArgumentNullException ("getter", "Either property getter or setter must be non-null.");
			Name = attachablePropertyName;
			VerifyGetter (getter);
			VerifyAdderSetter (setter);
			underlying_member = getter ?? setter;
			DeclaringType = schemaContext.GetXamlType (underlying_member.DeclaringType);
			target_type = schemaContext.GetXamlType (typeof (object));
			UnderlyingGetter = getter;
			UnderlyingSetter = setter;
			is_attachable = true;
		}

		[EnhancedXaml]
		public XamlMember(ParameterInfo parameterInfo, XamlSchemaContext schemaContext)
			: this(parameterInfo, schemaContext, null)
		{
		}

		[EnhancedXaml]
		public XamlMember(ParameterInfo parameterInfo, XamlSchemaContext schemaContext, XamlMemberInvoker invoker)
			: this(schemaContext, invoker)
		{
			var declaringType = schemaContext.GetXamlType (parameterInfo.Member.DeclaringType);
			Name = parameterInfo.Name;
			context = declaringType.SchemaContext;
			DeclaringType = declaringType;
			target_type = DeclaringType;
			type = schemaContext.GetXamlType (parameterInfo.ParameterType);
		}

		public XamlMember (string name, XamlType declaringType, bool isAttachable)
		{
			if (name == null)
				throw new ArgumentNullException ("name");
			if (declaringType == null)
				throw new ArgumentNullException ("declaringType");
			Name = name;
			context = declaringType.SchemaContext;
			DeclaringType = declaringType;
			target_type = DeclaringType;
			is_attachable = isAttachable;
		}

		XamlMember (XamlSchemaContext schemaContext, XamlMemberInvoker invoker)
		{
			if (schemaContext == null)
				throw new ArgumentNullException ("schemaContext");
			context = schemaContext;
			this.invoker = invoker;
		}

		internal XamlMember (bool isDirective, string ns, string name)
		{
			directive_ns = ns;
			Name = name;
			is_directive = isDirective;
		}


		internal MethodInfo UnderlyingGetter {
			get { return LookupUnderlyingGetter (); }
			private set { underlying_getter = value; }
		}
		internal MethodInfo UnderlyingSetter {
			get { return LookupUnderlyingSetter (); }
			private set { underlying_setter = value; }
		}

		public XamlType DeclaringType { get; private set; }
		public string Name { get; private set; }

		public string PreferredXamlNamespace {
			get { return directive_ns ?? (DeclaringType == null ? null : DeclaringType.PreferredXamlNamespace); }
		}

		#if !PCL
		public DesignerSerializationVisibility SerializationVisibility {
			get {
				var c= GetCustomAttributeProvider ();
				var a = c == null ? null : c.GetCustomAttribute<DesignerSerializationVisibilityAttribute> (false);
				return a != null ? a.Visibility : DesignerSerializationVisibility.Visible;
			}
		}
		#endif

		public bool IsAttachable {
			get { return is_attachable; }
		}

		public bool IsDirective {
			get { return is_directive; }
		}

		public bool IsNameValid {
			get { return flags.Get((int)MemberFlags.IsNameValid, () => XamlLanguage.IsValidXamlName (Name)); }
		}

		public XamlValueConverter<XamlDeferringLoader> DeferringLoader {
			get { return deferringLoader.Get(LookupDeferringLoader); }
		}
		
		static readonly XamlMember [] empty_members = new XamlMember [0];
		
		public IList<XamlMember> DependsOn {
			get { return LookupDependsOn () ?? empty_members; }
		}

		public XamlMemberInvoker Invoker {
			get { return invoker ?? (invoker = LookupInvoker()); }
		}

		public bool IsAmbient {
			get { return flags.Get((int)MemberFlags.IsAmbient, LookupIsAmbient); }
		}
		public bool IsEvent {
			get { return flags.Get((int)MemberFlags.IsEvent, LookupIsEvent); }
		}
		public bool IsReadOnly {
			get { return flags.Get((int)MemberFlags.IsReadOnly, LookupIsReadOnly); }
		}
		public bool IsReadPublic {
			get { return flags.Get((int)MemberFlags.IsReadPublic, LookupIsReadPublic); }
		}
		public bool IsUnknown {
			get { return flags.Get((int)MemberFlags.IsUnknown, LookupIsUnknown); }
		}
		public bool IsWriteOnly {
			get { return flags.Get((int)MemberFlags.IsWriteOnly, LookupIsWriteOnly); }
		}
		public bool IsWritePublic {
			get { return flags.Get((int)MemberFlags.IsWritePublic, LookupIsWritePublic); }
		}
		public XamlType TargetType {
			get { return targetType.Get(LookupTargetType); }
		}
		public XamlType Type {
			get { return type.Get(LookupType); }
		}

		public XamlValueConverter<TypeConverter> TypeConverter {
			get { return typeConverter.Get(LookupTypeConverter); }
		}
		public MemberInfo UnderlyingMember {
			get { return LookupUnderlyingMember (); }
		}

		public XamlValueConverter<ValueSerializer> ValueSerializer {
			get { return valueSerializer.Get(LookupValueSerializer); }
		}

		public static bool operator == (XamlMember left, XamlMember right)
		{
			return IsNull (left) ? IsNull (right) : left.Equals (right);
		}

		static bool IsNull (XamlMember a)
		{
			return Object.ReferenceEquals (a, null);
		}

		public static bool operator != (XamlMember left, XamlMember right)
		{
			return !(left == right);
		}
		
		public override bool Equals (object other)
		{
			var x = other as XamlMember;
			return Equals (x);
		}
		
		public bool Equals (XamlMember other)
		{
			// this should be in general correct; XamlMembers are almost not comparable.
			if (Object.ReferenceEquals (this, other))
				return true;
			// It does not compare XamlSchemaContext.
			return !IsNull (other) &&
				underlying_member == other.underlying_member &&
				underlying_getter == other.underlying_getter &&
				underlying_setter == other.underlying_setter &&
				Name == other.Name &&
				PreferredXamlNamespace == other.PreferredXamlNamespace &&
				directive_ns == other.directive_ns &&
				is_attachable == other.is_attachable;
		}

		public override int GetHashCode ()
		{
			if (is_attachable || string.IsNullOrEmpty(PreferredXamlNamespace))
			{
				if (DeclaringType == null)
					return Name.GetHashCode();
				else
					return DeclaringType.UnderlyingType.FullName.GetHashCode() ^ Name.GetHashCode();
			}
			else
				return PreferredXamlNamespace.GetHashCode() ^ DeclaringType.Name.GetHashCode() ^ Name.GetHashCode();
		}

		[MonoTODO ("there are some patterns that return different kind of value: e.g. List<int>.Capacity")]
		public override string ToString ()
		{
			if (is_attachable || String.IsNullOrEmpty (PreferredXamlNamespace)) {
				if (DeclaringType == null)
					return Name;
				else
					return String.Concat (DeclaringType.UnderlyingType.FullName, ".", Name);
			}
			else
				return String.Concat ("{", PreferredXamlNamespace, "}", DeclaringType.Name, ".", Name);
		}

		public virtual IList<string> GetXamlNamespaces ()
		{
			if (DeclaringType == null)
				return null;
			return DeclaringType.GetXamlNamespaces();
		}

		// lookups

		internal ICustomAttributeProvider GetCustomAttributeProvider ()
		{
			return customAttributeProvider.Get(LookupCustomAttributeProvider);
		}

		protected virtual ICustomAttributeProvider LookupCustomAttributeProvider ()
		{
			return UnderlyingMember != null ? context.GetCustomAttributeProvider(UnderlyingMember) : null;
		}

		protected virtual XamlValueConverter<XamlDeferringLoader> LookupDeferringLoader ()
		{
			var attr = GetCustomAttributeProvider()?.GetCustomAttribute<XamlDeferLoadAttribute>(true);
			if (attr == null)
				return Type?.DeferringLoader;
			var loaderType = attr.GetLoaderType();
			var contentType = attr.GetContentType();
			if (loaderType == null || contentType == null)
				throw new XamlSchemaException("Invalid metadata for attribute XamlDeferLoadAttribute");
			return new XamlValueConverter<XamlDeferringLoader>(loaderType, null); // why is the target type null here? thought it would be the contentType.
		}

		static readonly XamlMember [] empty_list = new XamlMember [0];

		protected virtual IList<XamlMember> LookupDependsOn ()
		{
			return empty_list;
		}

		protected virtual XamlMemberInvoker LookupInvoker ()
		{
			return new XamlMemberInvoker(this);
		}

		protected virtual bool LookupIsAmbient ()
		{
			var ambientAttribute = GetCustomAttributeProvider()?.GetCustomAttribute<AmbientAttribute>(true);
			return ambientAttribute != null;
		}

		protected virtual bool LookupIsEvent ()
		{
			return is_event;
		}

		protected virtual bool LookupIsReadOnly ()
		{
			return UnderlyingGetter != null && UnderlyingSetter == null;
		}
		protected virtual bool LookupIsReadPublic ()
		{
			if (underlying_member == null)
				return true;
			if (UnderlyingGetter != null)
				return UnderlyingGetter.IsPublic;
			return false;
		}

		protected virtual bool LookupIsUnknown ()
		{
			return underlying_member == null;
		}

		protected virtual bool LookupIsWriteOnly ()
		{
			var pi = underlying_member as PropertyInfo;
			if (pi != null)
				return !pi.CanRead && pi.CanWrite;
			return UnderlyingGetter == null && UnderlyingSetter != null;
		}

		protected virtual bool LookupIsWritePublic ()
		{
			if (underlying_member == null)
				return true;
			if (UnderlyingSetter != null)
				return UnderlyingSetter.IsPublic;
			return false;
		}

		protected virtual XamlType LookupTargetType ()
		{
			return target_type;
		}

		protected virtual XamlType LookupType ()
		{
			return context.GetXamlType (DoGetType ());
		}
		
		Type DoGetType ()
		{
			var pi = underlying_member as PropertyInfo;
			if (pi != null)
				return pi.PropertyType;
			var ei = underlying_member as EventInfo;
			if (ei != null)
				return ei.EventHandlerType;
			if (underlying_setter != null)
				return underlying_setter.GetParameters () [1].ParameterType;
			if (underlying_getter != null)
				return underlying_getter.GetParameters () [0].ParameterType;
			return typeof (object);
		}

		protected virtual XamlValueConverter<TypeConverter> LookupTypeConverter ()
		{
			var t = Type.UnderlyingType;
			if (t == null)
				return null;
			if (t == typeof (object)) // it is different from XamlType.LookupTypeConverter().
				return null;

			var a = GetCustomAttributeProvider ();
			var ca = a != null ? a.GetCustomAttribute<TypeConverterAttribute> (false) : null;
			if (ca != null)
				return context.GetValueConverter<TypeConverter> (System.Type.GetType (ca.ConverterTypeName), Type);

			return Type.TypeConverter;
		}

		protected virtual MethodInfo LookupUnderlyingGetter ()
		{
			return underlying_getter;
		}

		protected virtual MemberInfo LookupUnderlyingMember ()
		{
			return underlying_member;
		}

		protected virtual MethodInfo LookupUnderlyingSetter ()
		{
			return underlying_setter;
		}

		protected virtual XamlValueConverter<ValueSerializer> LookupValueSerializer ()
		{
			if (is_predefined_directive) // FIXME: this is likely a hack.
				return null;
			if (Type == null)
				return null;

			return XamlType.LookupValueSerializer (Type, GetCustomAttributeProvider()) ?? Type.ValueSerializer;
		}

		void VerifyGetter (MethodInfo method)
		{
			if (method == null)
				return;
			if (method.GetParameters ().Length != 1 || method.ReturnType == typeof (void))
				throw new ArgumentException (String.Format ("Property getter for {0} must have exactly one argument and must have non-void return type.", Name));
		}

		void VerifyAdderSetter (MethodInfo method)
		{
			if (method == null)
				return;
			if (method.GetParameters ().Length != 2)
				throw new ArgumentException (String.Format ("Property getter or event adder for {0} must have exactly one argument and must have non-void return type.", Name));
		}

		ReferenceValue<DefaultValueAttribute> defaultValue;
		internal DefaultValueAttribute DefaultValue
		{
			get
			{
				return defaultValue.Get(() => GetCustomAttributeProvider()?.GetCustomAttribute<DefaultValueAttribute>(true));
			}
		}

		internal bool IsConstructorArgument
		{
			get
			{
				return flags.Get((int)MemberFlags.IsConstructorArgument, () =>
					{
						var ap = GetCustomAttributeProvider();
						return ap != null && ap.GetCustomAttributes(typeof(ConstructorArgumentAttribute), false).Length > 0;
					});
			}
		}
	}
}
