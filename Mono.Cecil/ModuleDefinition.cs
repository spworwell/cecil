//
// ModuleDefinition.cs
//
// Author:
//   Jb Evain (jbevain@gmail.com)
//
// Copyright (c) 2008 - 2010 Jb Evain
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
using System.IO;
using SR = System.Reflection;

using Mono.Cecil.Cil;
using Mono.Cecil.Metadata;
using Mono.Cecil.PE;
using Mono.Collections.Generic;

namespace Mono.Cecil {

	public enum ReadingMode {
		Immediate = 1,
		Deferred = 2,
	}

	public sealed class ReaderParameters {

		ReadingMode reading_mode;
		IAssemblyResolver assembly_resolver;
		Stream symbol_stream;
		ISymbolReaderProvider symbol_reader_provider;
		bool read_symbols;

		public ReadingMode ReadingMode {
			get { return reading_mode; }
			set { reading_mode = value; }
		}

		public IAssemblyResolver AssemblyResolver {
			get { return assembly_resolver; }
			set { assembly_resolver = value; }
		}

		public Stream SymbolStream {
			get { return symbol_stream; }
			set { symbol_stream = value; }
		}

		public ISymbolReaderProvider SymbolReaderProvider {
			get { return symbol_reader_provider; }
			set { symbol_reader_provider = value; }
		}

		public bool ReadSymbols {
			get { return read_symbols; }
			set { read_symbols = value; }
		}

		public ReaderParameters ()
			: this (ReadingMode.Deferred)
		{
		}

		public ReaderParameters (ReadingMode readingMode)
		{
			this.reading_mode = readingMode;
		}
	}

#if !READ_ONLY

	public sealed class WriterParameters {

		Stream symbol_stream;
		ISymbolWriterProvider symbol_writer_provider;
		bool write_symbols;
#if !SILVERLIGHT
		SR.StrongNameKeyPair key_pair;
#endif
		public Stream SymbolStream {
			get { return symbol_stream; }
			set { symbol_stream = value; }
		}

		public ISymbolWriterProvider SymbolWriterProvider {
			get { return symbol_writer_provider; }
			set { symbol_writer_provider = value; }
		}

		public bool WriteSymbols {
			get { return write_symbols; }
			set { write_symbols = value; }
		}
#if !SILVERLIGHT
		public SR.StrongNameKeyPair StrongNameKeyPair {
			get { return key_pair; }
			set { key_pair = value; }
		}
#endif
	}

#endif

	public sealed class ModuleDefinition : ModuleReference, ICustomAttributeProvider {

		internal Image Image;
		internal TypeSystem TypeSystem;
		internal MetadataSystem MetadataSystem;
		internal ReadingMode ReadingMode;
		internal IAssemblyResolver AssemblyResolver;
		internal ISymbolReaderProvider SymbolReaderProvider;
		internal ISymbolReader SymbolReader;

		readonly MetadataReader reader;
		readonly string fq_name;

		internal ModuleKind kind;
		TargetRuntime runtime;
		TargetArchitecture architecture;
		ModuleAttributes attributes;
		Guid mvid;

		internal AssemblyDefinition assembly;
		MethodDefinition entry_point;

#if !READ_ONLY
		MetadataImporter importer;
#endif
		Collection<CustomAttribute> custom_attributes;
		Collection<AssemblyNameReference> references;
		Collection<ModuleReference> modules;
		Collection<Resource> resources;
		Collection<ExportedType> exported_types;
		TypeDefinitionCollection types;

		public bool IsMain {
			get { return kind != ModuleKind.NetModule; }
		}

		public ModuleKind Kind {
			get { return kind; }
		}

		public TargetRuntime Runtime {
			get { return runtime; }
			set { runtime = value; }
		}

		public TargetArchitecture Architecture {
			get { return architecture; }
			set { architecture = value; }
		}

		public ModuleAttributes Attributes {
			get { return attributes; }
			set { attributes = value; }
		}

		public string FullyQualifiedName {
			get { return fq_name; }
		}

		public Guid Mvid {
			get { return mvid; }
			set { mvid = value; }
		}

		internal bool HasImage {
			get { return Image != null; }
		}

		public bool HasSymbols {
			get { return SymbolReader != null; }
		}

		public override MetadataScopeType MetadataScopeType {
			get { return MetadataScopeType.ModuleDefinition; }
		}

		public AssemblyDefinition Assembly {
			get { return assembly; }
		}

#if !READ_ONLY
		internal MetadataImporter MetadataImporter {
			get {
				if (importer == null)
					importer = new MetadataImporter (this);

				return importer;
			}
		}
#endif
		public bool HasAssemblyReferences {
			get {
				if (references != null)
					return references.Count > 0;

				if (HasImage)
					return Image.HasTable (Table.AssemblyRef);

				return false;
			}
		}

		public Collection<AssemblyNameReference> AssemblyReferences {
			get {
				if (references != null)
					return references;

				if (HasImage)
					return references = Read (this, (_, reader) => reader.ReadAssemblyReferences ());

				return references = new Collection<AssemblyNameReference> ();
			}
		}

		public bool HasModuleReferences {
			get {
				if (modules != null)
					return modules.Count > 0;

				if (HasImage)
					return Image.HasTable (Table.ModuleRef);

				return false;
			}
		}

		public Collection<ModuleReference> ModuleReferences {
			get {
				if (modules != null)
					return modules;

				if (HasImage)
					return modules = Read (this, (_, reader) => reader.ReadModuleReferences ());

				return modules = new Collection<ModuleReference> ();
			}
		}

		public bool HasResources {
			get {
				if (resources != null)
					return resources.Count > 0;

				if (HasImage)
					return Image.HasTable (Table.ManifestResource) || Read (this, (_, reader) => reader.HasFileResource ());

				return false;
			}
		}

		public Collection<Resource> Resources {
			get {
				if (resources != null)
					return resources;

				if (HasImage)
					return resources = Read (this, (_, reader) => reader.ReadResources ());

				return resources = new Collection<Resource> ();
			}
		}

		public bool HasCustomAttributes {
			get {
				if (custom_attributes != null)
					return custom_attributes.Count > 0;

				return this.GetHasCustomAttributes (this);
			}
		}

		public Collection<CustomAttribute> CustomAttributes {
			get {
				if (custom_attributes != null)
					return custom_attributes;

				return this.GetCustomAttributes (ref custom_attributes, this);
			}
		}

		public bool HasTypes {
			get {
				if (types != null)
					return types.Count > 0;

				if (HasImage)
					return Image.HasTable (Table.TypeDef);

				return false;
			}
		}

		public Collection<TypeDefinition> Types {
			get {
				if (types != null)
					return types;

				if (HasImage)
					return types = Read (this, (_, reader) => reader.ReadTypes ());

				return types = new TypeDefinitionCollection (this);
			}
		}

		public bool HasExportedTypes {
			get {
				if (exported_types != null)
					return exported_types.Count > 0;

				if (HasImage)
					return Image.HasTable (Table.ExportedType);

				return false;
			}
		}

		public Collection<ExportedType> ExportedTypes {
			get {
				if (exported_types != null)
					return exported_types;

				if (HasImage)
					return exported_types = Read (this, (_, reader) => reader.ReadExportedTypes ());

				return exported_types = new Collection<ExportedType> ();
			}
		}

		public MethodDefinition EntryPoint {
			get {
				if (entry_point != null)
					return entry_point;

				if (HasImage)
					return entry_point = Read (this, (_, reader) => reader.ReadEntryPoint ());

				return entry_point = null;
			}
			set { entry_point = value; }
		}

		internal ModuleDefinition ()
		{
			this.TypeSystem = TypeSystem.CreateTypeSystem (this);
			this.MetadataSystem = new MetadataSystem ();
			this.token = new MetadataToken (TokenType.Module, 1);
			this.AssemblyResolver = GlobalAssemblyResolver.Instance;
		}

		internal ModuleDefinition (Image image)
			: this ()
		{
			this.Image = image;
			this.kind = image.Kind;
			this.runtime = image.Runtime;
			this.architecture = image.Architecture;
			this.attributes = image.Attributes;
			this.fq_name = image.FileName;

			this.reader = new MetadataReader (this);
		}

		public bool HasTypeReference (string fullName)
		{
			return HasTypeReference (string.Empty, fullName);
		}

		public bool HasTypeReference (string scope, string fullName)
		{
			CheckFullName (fullName);

			return Read (this, (_, reader) => reader.GetTypeReference (scope, fullName) != null);
		}

		public bool TryGetTypeReference (string fullName, out TypeReference type)
		{
			return TryGetTypeReference (string.Empty, fullName, out type);
		}

		public bool TryGetTypeReference (string scope, string fullName, out TypeReference type)
		{
			CheckFullName (fullName);

			return (type = Read (this, (_, reader) => reader.GetTypeReference (scope, fullName))) != null;
		}

		public TypeDefinition GetType (string fullName)
		{
			CheckFullName (fullName);

			var position = fullName.IndexOf ('/');
			if (position > 0)
				return GetNestedType (fullName);

			return ((TypeDefinitionCollection) this.Types).GetType (fullName);
		}

		static void CheckFullName (string fullName)
		{
			if (fullName == null)
				throw new ArgumentNullException ("fullName");
			if (fullName.Length == 0)
				throw new ArgumentException ();
		}

		TypeDefinition GetNestedType (string fullname)
		{
			var names = fullname.Split ('/');
			var type = GetType (names [0]);

			for (int i = 1; i < names.Length; i++) {
				var nested_types = type.NestedTypes;
				for (int j = 0; j < nested_types.Count; j++) {
					var nested_type = nested_types [j];
					if (nested_type.Name != names [i])
						continue;

					type = nested_type;
					break;
				}
			}

			return type;
		}

		internal FieldDefinition Resolve (FieldReference field)
		{
			return MetadataResolver.Resolve (AssemblyResolver, field);
		}

		internal MethodDefinition Resolve (MethodReference method)
		{
			return MetadataResolver.Resolve (AssemblyResolver, method);
		}

		internal TypeDefinition Resolve (TypeReference type)
		{
			return MetadataResolver.Resolve (AssemblyResolver, type);
		}

#if !READ_ONLY

		static void CheckType (object type)
		{
			if (type == null)
				throw new ArgumentNullException ("type");
		}

		static void CheckField (object field)
		{
			if (field == null)
				throw new ArgumentNullException ("field");
		}

		static void CheckMethod (object method)
		{
			if (method == null)
				throw new ArgumentNullException ("method");
		}

		public TypeReference Import (Type type)
		{
			CheckType (type);

			return MetadataImporter.ImportType (type, null);
		}

		public FieldReference Import (SR.FieldInfo field)
		{
			CheckField (field);

			return MetadataImporter.ImportField (field);
		}

		public MethodReference Import (SR.MethodBase method)
		{
			CheckMethod (method);

			return MetadataImporter.ImportMethod (method);
		}

		public TypeReference Import (TypeReference type)
		{
			CheckType (type);

			if (type.Module == this)
				return type;

			return MetadataImporter.ImportType (type, null);
		}

		public FieldReference Import (FieldReference field)
		{
			CheckField (field);

			if (field.Module == this)
				return field;

			return MetadataImporter.ImportField (field);
		}

		public MethodReference Import (MethodReference method)
		{
			CheckMethod (method);

			if (method.Module == this)
				return method;

			return MetadataImporter.ImportMethod (method);
		}

#endif

		public IMetadataTokenProvider LookupToken (int token)
		{
			return LookupToken (new MetadataToken ((uint) token));
		}

		public IMetadataTokenProvider LookupToken (MetadataToken token)
		{
			return Read (this, (_, reader) => reader.LookupToken (token));
		}

		internal TRet Read<TItem, TRet> (TItem item, Func<TItem, MetadataReader, TRet> read)
		{
			var position = reader.position;
			var context = reader.context;

			var ret = read (item, reader);

			reader.position = position;
			reader.context = context;

			return ret;
		}

		void ProcessDebugHeader ()
		{
			if (Image == null || Image.Debug.IsZero)
				return;

			byte [] header;
			var directory = Image.GetDebugHeader (out header);

			if (!SymbolReader.ProcessDebugHeader (directory, header))
				throw new InvalidOperationException ();
		}

#if !READ_ONLY

		public static ModuleDefinition CreateModule (string name, ModuleKind kind)
		{
			return CreateModule (name, kind, GetCurrentRuntime ());
		}

		public static ModuleDefinition CreateModule (string name, ModuleKind kind, TargetRuntime runtime)
		{
			return CreateModule (name, kind, runtime, TargetArchitecture.I386);
		}

		public static ModuleDefinition CreateModule (string name, ModuleKind kind, TargetRuntime runtime, TargetArchitecture architecture)
		{
			Mixin.CheckName (name);

			var module = new ModuleDefinition {
				Name = name,
				kind = kind,
				runtime = runtime,
				architecture = architecture,
				mvid = Guid.NewGuid (),
				Attributes = ModuleAttributes.ILOnly,
			};

			if (kind != ModuleKind.NetModule) {
				var assembly = new AssemblyDefinition ();
				module.assembly = assembly;
				module.assembly.Name = new AssemblyNameDefinition (name, new Version ());
				assembly.main_module = module;
			}

			module.Types.Add (new TypeDefinition (string.Empty, "<Module>", TypeAttributes.NotPublic));

			return module;
		}

		static TargetRuntime GetCurrentRuntime ()
		{
			var corlib_version = typeof (object).Assembly.GetName ().Version;
			switch (corlib_version.Major) {
			case 1:
				return corlib_version.Minor == 0
					? TargetRuntime.Net_1_0
					: TargetRuntime.Net_1_1;
			case 2:
				return TargetRuntime.Net_2_0;
			case 4:
				return TargetRuntime.Net_4_0;
			default:
				throw new NotSupportedException ();
			}
		}

#endif

		public void ReadSymbols (ISymbolReader reader)
		{
			if (reader == null)
				throw new ArgumentNullException ("reader");

			SymbolReader = reader;

			ProcessDebugHeader ();
		}

		public static ModuleDefinition ReadModule (string fileName)
		{
			return ReadModule (fileName, new ReaderParameters (ReadingMode.Deferred));
		}

		public static ModuleDefinition ReadModule (Stream stream)
		{
			return ReadModule (stream, new ReaderParameters (ReadingMode.Deferred));
		}

		public static ModuleDefinition ReadModule (string fileName, ReaderParameters parameters)
		{
			using (var stream = GetFileStream (fileName, FileMode.Open, FileAccess.Read, FileShare.Read)) {
				return ReadModule (stream, parameters);
			}
		}

		static void CheckParameters (object parameters)
		{
			if (parameters == null)
				throw new ArgumentNullException ("parameters");
		}

		static void CheckStream (object stream)
		{
			if (stream == null)
				throw new ArgumentNullException ("stream");
		}

		public static ModuleDefinition ReadModule (Stream stream, ReaderParameters parameters)
		{
			CheckStream (stream);
			if (!stream.CanRead || !stream.CanSeek)
				throw new ArgumentException ();
			CheckParameters (parameters);

			return ModuleReader.CreateModuleFrom (
				ImageReader.ReadImageFrom (stream),
				parameters);
		}

		static Stream GetFileStream (string fileName, FileMode mode, FileAccess access, FileShare share)
		{
			if (fileName == null)
				throw new ArgumentNullException ("fileName");
			if (fileName.Length == 0)
				throw new ArgumentException ();

			return new FileStream (fileName, mode, access, share);
		}

#if !READ_ONLY

		public void Write (string fileName)
		{
			Write (fileName, new WriterParameters ());
		}

		public void Write (Stream stream)
		{
			Write (stream, new WriterParameters ());
		}

		public void Write (string fileName, WriterParameters parameters)
		{
			using (var stream = GetFileStream (fileName, FileMode.Create, FileAccess.ReadWrite, FileShare.None)) {
				Write (stream, parameters);
			}
		}

		public void Write (Stream stream, WriterParameters parameters)
		{
			CheckStream (stream);
			if (!stream.CanWrite || !stream.CanSeek)
				throw new ArgumentException ();
			CheckParameters (parameters);

			ModuleWriter.WriteModuleTo (this, stream, parameters);
		}

#endif

	}

	static partial class Mixin {

		public static bool HasImage (this ModuleDefinition self)
		{
			return self != null && self.HasImage;
		}

		public static string GetFullyQualifiedName (this Stream stream)
		{
			var file_stream = stream as FileStream;
			if (file_stream == null)
				return string.Empty;

			return Path.GetFullPath (file_stream.Name);
		}
	}
}