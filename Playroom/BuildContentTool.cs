using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ToolBelt;
using System.Xml;
using System.Reflection;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using TsonLibrary;

namespace Playroom
{
	[CommandLineTitle("Playroom Content Builder")]
	[CommandLineDescription("A tool for compiling game and application content from raw resources")]
	[CommandLineCopyright("Copyright (c) 2013, Jamoki LLC")]
	[CommandLineCommandDescription("build", Description = "Builds content from using a .contents file")]
	[CommandLineCommandDescription("clean", Description = "Cleans content using a .contents file")]
	[CommandLineCommandDescription("new", Description = "Creates a new bare bones .contents file")]
	[CommandLineCommandDescription("help", Description = "Displays help for this tool ")]
	public class BuildContentTool : ToolBase
	{
		#region Fields
		private BuildContext buildContext = null;

        #endregion

        #region Construction
		public BuildContentTool()
		{
		}

        #endregion

		[CommandCommandLineArgument(Description = "Mode to execute in.  Can be build, clean, help, new.", Commands = "help,build,clean,new")]
		public string Command { get; set; }

		[DefaultCommandLineArgument(
			Description = "Input .content data file", ValueHint = "<content-file>",
			Commands = "build,help,clean,new")]
		public ParsedFilePath ContentPath { get; set; }

		[CommandLineArgument(
			"properties", ShortName = "p", Description = "Additional properties to set", 
			ValueHint = "<prop1=val1;prop2=val2>",
			Commands = "build,clean")]
		public string Properties { get; set; }

		[CommandLineArgument(
			"test", ShortName = "t", Description = "Show what would be built without actually building it",
			Commands = "build")]
		public bool TestOnly { get; set; }
		
		[CommandLineArgument(
			"force", ShortName = "f", Description = "Force a build where every source file is out of date.",
			Commands = "build")]
		public bool Force { get; set; }

		[CommandLineArgument("debug", ShortName = "d", Description="Show property information while building", Commands="build")]
		public bool ShowProperties { get; set; }
		
		[CommandLineArgument("help", ShortName = "?", Description="Displays this help")]
		public bool ShowHelp { get; set; }
		
		[CommandLineArgument("nologo", Description = "Suppress display of logo/banner", Commands = "build, clean")]
		public bool NoLogo { get; set; }

		public override void Execute()
		{
			try
			{
				if (!NoLogo)
					Console.WriteLine(Parser.LogoBanner);
				
				bool hasContentFile = !String.IsNullOrEmpty(ContentPath);

				if ((!hasContentFile && this.Command == "help") ||
					(String.IsNullOrEmpty(this.Command) && this.ShowHelp))
				{
					Console.WriteLine(Parser.Usage);
					return;
				}
				
				if (!hasContentFile)
				{
					WriteError("A .content file must be specified");
					return;
				}
				
                this.ContentPath = new ParsedFilePath(this.ContentPath.MakeFullPath());

				if (this.Command == "new")
				{
					CreateContentFileFromTemplate();
					return;
				} 

				if (!File.Exists(this.ContentPath))
				{
					WriteError("Content file '{0}' does not exist", this.ContentPath);
					return;
				}
				
				buildContext = new BuildContext(this.Properties, this.ContentPath);

				ApplyCompilerSettingsExtensions();

				if (this.Command == "help")
				{
					WriteContentCompilerUsage();
					return;
				}

				List<BuildTarget> buildTargets;

				PrepareBuildTargets(out buildTargets);

				if (this.Command == "clean")
				{
					Clean(buildTargets);
				}
				else
				{
					Build(buildTargets);
				}

				WriteMessage("Done");
			}
			catch (Exception e)
			{
                TextLocation? mark = null;

				do
				{
                    ContentFileException cfe = e as ContentFileException;
                    TsonParseException tpe = e as TsonParseException;

                    if (cfe != null)
                    {
                        mark = cfe.ErrorLocation;
                    } 
                    else if (tpe != null)
                    {
                        mark = tpe.ErrorLocation;
                    }

					// If we started showing content file errors, keep going... 
					if (mark.HasValue)
                        WriteErrorWithLine(ContentPath, mark.Value.Line + 1, mark.Value.Column + 1, e.Message);
					else
						ConsoleUtility.WriteMessage(MessageType.Error, e.Message);
#if DEBUG
					// Gotta have this in debug builds
					Console.WriteLine(e.StackTrace);
#endif
				}
				while ((e = e.InnerException) != null);
			}
		}

        #region Private Methods

        private void WriteErrorWithLine(string fileName, int line, int column, string message)
        {
            ConsoleUtility.WriteMessage(MessageType.Error, "{0}({1},{2}): {3}", fileName, line, column, message);
        }

		private void CreateContentFileFromTemplate()
		{
			// Get this type's assembly
			Assembly assem = this.GetType().Assembly;
			
			// Load the resource using a namespace
			using (StreamReader reader = new StreamReader(assem.GetManifestResourceStream(this.GetType(), "Template.content")))
			{
				File.WriteAllText(this.ContentPath, reader.ReadToEnd());
			}
		}

		private void WriteContentCompilerUsage()
		{
			foreach (var compilerClass in buildContext.CompilerClasses)
			{
				Console.WriteLine("\nCompiler '{0}':", compilerClass.Name);

				Console.WriteLine("  Extensions:");

				if (compilerClass.Extensions.Count == 0)
				{
					Console.WriteLine ("    None");
					continue;
				}

				foreach (var extension in compilerClass.Extensions)
				{
					Console.WriteLine("    {0} -> {1}", extension.Input, extension.Output);
				}

				if (compilerClass.CompilerParameters.Count > 0)
				{
					Console.WriteLine("  Compiler Parameters:");
					WriteParameters(compilerClass, compilerClass.CompilerParameters);
				}

				if (compilerClass.TargetParameters.Count > 0)
				{
					Console.WriteLine("  Target Paramaters:");
					WriteParameters(compilerClass, compilerClass.TargetParameters);
				}
			}
		}

		private void WriteParameters(CompilerClass compilerClass, Dictionary<string, AttributedProperty> settings)
		{
			foreach (var pair in settings)
			{
				string description;
								
				if (pair.Value.Attribute.Optional)
				{
					string def = compilerClass.Type.GetProperty(pair.Key).GetValue(compilerClass.Instance, null).ToString();

					description = "{0} (optional, default \"{1}\");".CultureFormat(
						pair.Value.Attribute.Description, def);
					
					Console.Write(
						"    {0,-15}{1,-15}", 
						pair.Key, 
						pair.Value.Property.PropertyType.Name);
				}
				else
				{
					description = "{0} (required)".CultureFormat(pair.Value.Attribute.Description);
					
					Console.Write(
						"    {0,-15}{1,-15}", 
						pair.Key, 
						pair.Value.Property.PropertyType.Name);
				}
				
				int indent = 4 + 15 + 15;
				string[] lines = description.WordWrap(79 - indent);
				int i = 0;
				
				Console.WriteLine(lines[i++]);
				
				for (; i < lines.Length; i++)
				{
					Console.WriteLine(new String(' ', indent) + lines[i]);
				}
			}
		}

		private void WriteProperties(PropertyCollection properties)
		{
			WriteMessage("  Properties:");

			foreach (KeyValuePair<string, string> pair in properties)
			{
				WriteMessage("    {0} = {1}", pair.Key, pair.Value);
			}
		}

		private void PrepareBuildTargets(out List<BuildTarget> buildTargets)
		{
			buildTargets = new List<BuildTarget>();
			
			foreach (var rawTarget in buildContext.ContentFile.Targets)
			{
				try
				{
					buildTargets.Add(new BuildTarget(rawTarget, buildContext));
				}
				catch (Exception e)
				{
					throw new ContentFileException(rawTarget.Name, "Error preparing to build targets", e);
				}
			}
			
			buildTargets = TopologicallySortBuildTargets(buildTargets);
		}
		
		private List<BuildTarget> TopologicallySortBuildTargets(List<BuildTarget> targets)
		{
			// Create a dictionary of paths -> targets for which they are an input to speed up building the graph
			Dictionary<ParsedPath, List<BuildTarget>> inputPaths = new Dictionary<ParsedPath, List<BuildTarget>>();
			
			foreach (var target in targets)
			{
				foreach (var path in target.InputPaths)
				{
					List<BuildTarget> inputTargets;
					
					if (!inputPaths.TryGetValue(path, out inputTargets))
					{
						inputTargets = new List<BuildTarget>();
						inputPaths.Add(path, inputTargets);
					}
					
					inputTargets.Add(target);
				}
			}
			
			// Create an adjacency list to represent the graph of from -> to targets
			Dictionary<BuildTarget, HashSet<BuildTarget>> graph = new Dictionary<BuildTarget, HashSet<BuildTarget>>();
			Dictionary<BuildTarget, int> inputEdgeCounts = new Dictionary<BuildTarget, int>();
			
			targets.ForEach(item => graph.Add(item, new HashSet<BuildTarget>()));
			targets.ForEach(item => inputEdgeCounts[item] = 0);
			
			foreach (var fromTarget in targets)
			{
				foreach (var outputPath in fromTarget.OutputPaths)
				{
					List<BuildTarget> outputTargets;
					
					if (inputPaths.TryGetValue(outputPath, out outputTargets))
					{
						// The from target has an output path which is an input path to other target(s),
						// so add edges from the from target to each of those other targets
						foreach (var outputTarget in outputTargets)
						{
							HashSet<BuildTarget> toTargets = graph[fromTarget];
							toTargets.Add(outputTarget);
							inputEdgeCounts[outputTarget]++;
						}
					}
				}
			}
			
			Queue<BuildTarget> rootTargets = new Queue<BuildTarget>();
			List<BuildTarget> orderedTargets = new List<BuildTarget>();
			
			foreach (var buildTarget in targets)
			{
				if (inputEdgeCounts[buildTarget] == 0)
					rootTargets.Enqueue(buildTarget);
			}
			
			// Do the sort
			while (rootTargets.Count != 0)
			{
				BuildTarget fromTarget = rootTargets.Dequeue();
				
				orderedTargets.Add(fromTarget);
				
				HashSet<BuildTarget> toTargets = graph[fromTarget];
				
				graph.Remove(fromTarget);
				
				foreach (var toTarget in toTargets)
				{
					inputEdgeCounts[toTarget]--;
					
					if (inputEdgeCounts[toTarget] == 0)
					{
						rootTargets.Enqueue(toTarget);
					}
				}
			}
			
			if (graph.Count != 0)
			{
				throw new ArgumentException("A circular target dependency exists starting at target '{0}'".CultureFormat(graph.First().Key.Name));
			}
			
			return orderedTargets;
		}

		private void ApplyCompilerSettingsExtensions()
		{
			foreach (var compilerClass in buildContext.CompilerClasses)
			{
				// Get the compiler setup entry for this compiler if there is one
				ContentFileV4.CompilerSetting rawSetup = buildContext.ContentFile.CompilerSettings.FirstOrDefault(s => compilerClass.Name.EndsWith(s.Name.Value));
				
				// If there are extensions in the settings try and set the Extensions property
				if (rawSetup == null || rawSetup.CompilerExtensions.Count == 0)
				{
					if (compilerClass.ExtensionsProperty.CanWrite)
						// TODO: Make this a ContentFileException pointing to the Compilers section
						throw new ApplicationException("Compiler '{0}' expects Extensions and none are set".CultureFormat(compilerClass.Name));
					else
						continue;
				} 

				if (!compilerClass.ExtensionsProperty.CanWrite)
				{
					throw new ContentFileException(rawSetup.Name, "Unable to write to Extensions property of '{0}' compiler".CultureFormat(compilerClass.Name));
				}
				
				try
				{
					List<CompilerExtension> extensions = new List<CompilerExtension>();
					
					for (int i = 0; i < rawSetup.CompilerExtensions.Count; i++)
					{
						var rawExtensions = rawSetup.CompilerExtensions[i];
						extensions.Add(new CompilerExtension(rawExtensions.Inputs, rawExtensions.Outputs));
					}
					
					compilerClass.ExtensionsProperty.SetValue(compilerClass.Instance, extensions, null);
				}
				catch (Exception)
				{
					throw new ContentFileException(rawSetup.Name, "Invalid compiler extensions specified for '{0}' compiler".CultureFormat(compilerClass.Name));
				}
			}
		}
		
		private void ApplyCompilerSettingsParameters()
		{
			foreach (var compilerClass in buildContext.CompilerClasses)
			{
				// Get the compiler setup entry for this compiler if there is one
				var rawCompilerSettings = buildContext.ContentFile.CompilerSettings.FirstOrDefault(s => compilerClass.Name.EndsWith(s.Name.Value));

				ApplyParameters(
					rawCompilerSettings.Name, 
					compilerClass.Name, 
					compilerClass.Instance, 
					compilerClass.CompilerParameters, 
					rawCompilerSettings.Parameters);
			}
		}
		
		private void ApplyParameters(
			TsonNode parentNode, 
			string compilerName, 
			object instance, 
			Dictionary<string, AttributedProperty> attrProps, 
			TsonObjectNode parameterNode)
		{
			HashSet<string> required = 
				new HashSet<string>(
					from s in attrProps
					where s.Value.Attribute.Optional == false
					select s.Key);
			
            foreach (var keyValue in parameterNode.KeyValues)
			{
				var keyNode = keyValue.Key;
				var valueNode = keyValue.Value;
				AttributedProperty attrProp;
				
                attrProps.TryGetValue(keyNode.Value, out attrProp);
				
				if (attrProp == null)
				{
					WriteWarning("Supplied parameter '{0}' is not applicable to the '{1}' compiler".CultureFormat(keyNode.Value, compilerName));
					continue;
				}
				
				PropertyInfo propertyInfo = attrProp.Property;
				
				if (!propertyInfo.CanWrite)
                    throw new ContentFileException(parentNode, "Unable to write to the '{0}' property of '{1}' compiler".CultureFormat(keyNode.Value, compilerName));
				
				object obj = null;

				if (propertyInfo.PropertyType == typeof(double))
				{
                    var numberNode = valueNode as TsonNumberNode;

                    if (numberNode == null)
                        throw new ContentFileException(valueNode, "TSON node for '{0}' is not a number".CultureFormat(keyNode.Value));

                    obj = numberNode.Value;
				}
                else if (propertyInfo.PropertyType == typeof(string))
                {
                    var numberNode = valueNode as TsonStringNode;

                    if (numberNode == null)
                        throw new ContentFileException(valueNode, "TSON node for '{0}' is not a string".CultureFormat(keyNode.Value));

                    obj = numberNode.Value;
                }
                else if (propertyInfo.PropertyType == typeof(bool))
                {
                    obj = ((TsonBooleanNode)valueNode).Value;
                }
				else
				{
					throw new ContentFileException(
						parameterNode, 
                        "Setting '{0}' parameter for compiler '{1}' must be bool, double or string".CultureFormat(keyNode.Value, compilerName));
				}
				
				try
				{
					propertyInfo.SetValue(instance, obj, null);
				}
				catch (Exception e)
				{
					throw new ContentFileException(keyValue.Key, "Error setting compiler property", e);
				}
				
				required.Remove(keyNode.Value);
			}

			if (required.Count != 0)
				throw new ContentFileException(
					parentNode, 
					"Required parameter '{0}' of compiler '{1}' was not set".CultureFormat(required.First(), compilerName));
		}

		private void Clean(List<BuildTarget> buildTargets)
		{
			foreach (var buildTarget in buildTargets)
			{
				WriteMessage("Cleaning target '{0}'", buildTarget.Name);

				foreach (var outputPath in buildTarget.OutputPaths)
				{
					if (File.Exists(outputPath))
					{
						File.Delete(outputPath);
						WriteMessage("\tDeleted '{0}'", outputPath);
					}
				}
			}

			string hashPath = buildContext.ContentFileHashesPath;

			if (File.Exists(hashPath))
			{
				File.Delete(hashPath);
				WriteMessage("Deleted content hash file '{0}'", hashPath);
			}
		}

		private void Build(List<BuildTarget> buildTargets)
		{
			string oldGlobalHash;
			HashSet<string> oldTargetHashes;

			ReadOldContentFileHashes(out oldGlobalHash, out oldTargetHashes);

			if (ShowProperties)
			{
				WriteProperties(buildContext.Properties);
			}

			foreach (var buildTarget in buildTargets)
			{
				foreach (var inputPath in buildTarget.InputPaths)
				{
					if (!File.Exists(inputPath))
					{
						throw new ContentFileException("Required input file '{0}' does not exist".CultureFormat(inputPath));
					}
				}

				if (!IsCompileRequired(buildTarget, oldGlobalHash, oldTargetHashes))
					continue;

				CompilerClass compilerClass = buildTarget.CompilerClass;
				string msg = String.Format("Building target '{0}' with '{1}' compiler", buildTarget.Name, compilerClass.Name);

				foreach (var input in buildTarget.InputPaths)
				{
					msg += Environment.NewLine + "  " + input;
				}
				msg += Environment.NewLine + "  ->";
				foreach (var output in buildTarget.OutputPaths)
				{
					msg += Environment.NewLine + "  " + output;
				}
				WriteMessage(msg);

				if (ShowProperties)
				{
					WriteProperties(buildTarget.Properties);
				}

				if (TestOnly)
					continue;

				// Set the Context and Target properties on the Compiler class instance
				compilerClass.ContextProperty.SetValue(compilerClass.Instance, buildContext, null);
				compilerClass.TargetProperty.SetValue(compilerClass.Instance, buildTarget, null);

				// Set all target properties
				ApplyParameters(
					buildTarget.RawTarget.Name, 
					compilerClass.Name,
					compilerClass.Instance,
					compilerClass.TargetParameters, 
					buildTarget.RawTarget.Parameters);

				try
				{
					compilerClass.CompileMethod.Invoke(compilerClass.Instance, null);
				}
				catch (TargetInvocationException e)
				{
					throw new ContentFileException(
						buildTarget.RawTarget.Name, "Unable to compile target '{0}'".CultureFormat(buildTarget.Name), e.InnerException);
				}

				// Ensure that the output files were generated
				foreach (var outputFile in buildTarget.OutputPaths)
				{
					if (!File.Exists(outputFile))
					{
						throw new ContentFileException(
                            buildTarget.RawTarget.Name, "Output file '{0}' was not generated".CultureFormat(outputFile));
					}
				}
			}

			WriteNewContentFileHashes(buildTargets);
		}

		private void ReadOldContentFileHashes(out string oldGlobalHash, out HashSet<string> oldTargetHashes)
		{
			oldGlobalHash = String.Empty;
			oldTargetHashes = new HashSet<string>();

			if (File.Exists(buildContext.ContentFileHashesPath))
			{
				try
				{
                    var hashes = TsonSerializer.Deserialize<ContentFileHashesFile>(
                        File.ReadAllText(buildContext.ContentFileHashesPath));
					
					oldGlobalHash = hashes.Global;

					foreach (var hash in hashes.Targets)
					{
						oldTargetHashes.Add(hash);
					}
				}
				catch
				{
					// Bad file, don't use it again
					File.Delete(buildContext.ContentFileHashesPath);
				}
			}
		}

		private void WriteNewContentFileHashes(List<BuildTarget> buildTargets)
		{
			ContentFileHashesFile hashes = new ContentFileHashesFile()
			{
				Global = buildContext.GlobalHash,
				Targets = buildTargets.Select(t => t.Hash).ToArray()
			};

			try
			{
                var tson = TsonSerializer.Serialize(hashes);
				
                File.WriteAllText(buildContext.ContentFileHashesPath, tson);
			}
			catch
			{
				WriteWarning("Unable to write content hash file '{0}'".CultureFormat(buildContext.ContentFileHashesPath));
			}
		}

		private bool IsCompileRequired(BuildTarget buildTarget, string oldGlobalHash, HashSet<string> oldTargetHashes)
		{
			if (this.Force)
				return true;

			DateTime lastWriteTime;
			DateTime newestInputFile = buildContext.NewestAssemblyWriteTime;

			foreach (var inputPath in buildTarget.InputPaths)
			{
				lastWriteTime = File.GetLastWriteTime(inputPath);

				if (lastWriteTime > newestInputFile)
					newestInputFile = lastWriteTime;
			}

			DateTime oldestOutputFile = DateTime.MaxValue;

			foreach (var outputPath in buildTarget.OutputPaths)
			{
				lastWriteTime = File.GetLastWriteTime(outputPath);

				if (lastWriteTime < oldestOutputFile)
					oldestOutputFile = lastWriteTime;
			}

			// And last but not least, if the content file is newer than all inputs so far and this targets hash 
			// is not present in the hash file then the definition changed or was added,
			// OR if the global hash has changed then consider the content file write time.

			if (buildContext.ContentFileWriteTime > newestInputFile && 
				(oldGlobalHash != buildContext.GlobalHash || !oldTargetHashes.Contains(buildTarget.Hash)))
			{
				newestInputFile = buildContext.ContentFileWriteTime;
			}

			return newestInputFile > oldestOutputFile;
		}

        #endregion
	}
}
