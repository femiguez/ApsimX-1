﻿namespace Models.Core.ApsimFile
{
    using APSIM.Shared.Utilities;
    using System;
    using System.IO;
    using System.Reflection;
    using System.Linq;
    using Newtonsoft.Json;
    using System.Xml;
    using Newtonsoft.Json.Serialization;
    using System.Collections.Generic;
    using System.Xml.Serialization;
    using Models.Core.Interfaces;
    using Newtonsoft.Json.Linq;

    /// <summary>
    /// A class for reading and writing the .apsimx file format.
    /// </summary>
    /// <remarks>
    /// Features:
    /// * Can WRITE a model in memory to an APSIM Next Generation .json string.
    ///     - Only writes public, settable, properties of a model.
    ///     - If a model implements IDontSerialiseChildren then no child models will be serialised.
    ///     - Won't serialise any property with XmlIgnore attribute.
    /// * Can READ an APSIM Next Generation JSON or XML string to models in memory.
    ///     - Calls converter on the string before deserialisation.
    ///     - Sets fileName property in all simulation models read in.
    ///     - Correctly parents all models.
    ///     - Calls IModel.OnCreated() for all newly created models. If models throw in the
    ///       OnCreated() method, exceptions will be captured and returned to caller along
    ///       with the model tree.
    /// </remarks>
    public class FileFormat
    {
        /// <summary>Convert a model to a string (json).</summary>
        /// <param name="model">The model to serialise.</param>
        /// <returns>The json string.</returns>
        public static string WriteToString(IModel model)
        {
            JsonSerializer serializer = new JsonSerializer()
            {
                DateParseHandling = DateParseHandling.None,
                TypeNameHandling = TypeNameHandling.Objects,
                ContractResolver = new WritablePropertiesOnlyResolver(),
                Formatting = Newtonsoft.Json.Formatting.Indented
            };
            string json;
            using (StringWriter s = new StringWriter())
                using (var writer = new JsonTextWriter(s))
                {
                    serializer.Serialize(writer, model, model.GetType());
                    json = s.ToString();
                }
            return json;
        }

        /// <summary>Create a simulations object by reading the specified filename</summary>
        /// <param name="fileName">Name of the file.</param>
        /// <param name="creationExceptions">A list of exceptions created during creation of the models.</param>
        public static T ReadFromFile<T>(string fileName, out List<Exception> creationExceptions) where T : IModel
        {
            if (!File.Exists(fileName))
                throw new Exception("Cannot read file: " + fileName + ". File does not exist.");

            string contents = File.ReadAllText(fileName);
            T newModel = ReadFromString<T>(contents, out creationExceptions, fileName);

            // Set the filename
            if (newModel is Simulations)
                (newModel as Simulations).FileName = fileName;
            Apsim.ChildrenRecursively(newModel, typeof(Simulation)).ForEach(m => (m as Simulation).FileName = fileName);
            return newModel;
        }

        /// <summary>Convert a string (json or xml) to a model.</summary>
        /// <param name="st">The string to convert.</param>
        /// <param name="creationExceptions">A list of exceptions created during creation of the models.</param>
        /// <param name="fileName">The optional filename where the string came from.</param>
        public static T ReadFromString<T>(string st, out List<Exception> creationExceptions, string fileName = null) where T : IModel
        {
            // Run the converter.
            var converter = Converter.DoConvert(st, -1, fileName);

            T newModel;
            JsonSerializerSettings settings = new JsonSerializerSettings()
            {
                TypeNameHandling = TypeNameHandling.Auto,
                DateParseHandling = DateParseHandling.None
            };
            newModel = JsonConvert.DeserializeObject<T>(converter.Root.ToString(), settings);

            if (newModel is Simulations)
                (newModel as Simulations).FileName = fileName;

            // Parent all models.
            newModel.Parent = null;
            Apsim.ParentAllChildren(newModel);

            // Call created in all models.
            creationExceptions = new List<Exception>();
            foreach (var model in Apsim.ChildrenRecursively(newModel))
            {
                try
                {
                    model.OnCreated();
                }
                catch (Exception err)
                {
                    creationExceptions.Add(err);
                }
            }
            return newModel;
        }

        /// <summary>A contract resolver class to only write settable properties.</summary>
        private class WritablePropertiesOnlyResolver : DefaultContractResolver
        {
            protected override List<MemberInfo> GetSerializableMembers(Type objectType)
            {
                var result = base.GetSerializableMembers(objectType);
                result.RemoveAll(m => m is PropertyInfo &&
                                      !(m as PropertyInfo).CanWrite);
                result.RemoveAll(m => m.GetCustomAttribute(typeof(XmlIgnoreAttribute)) != null);
                return result;
            }

            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                JsonProperty property = base.CreateProperty(member, memberSerialization);

                if (property.PropertyName == "Children")
                    property.ShouldSerialize = instance =>
                    {
                        if (instance is IOptionallySerialiseChildren)
                            return (instance as IOptionallySerialiseChildren).DoSerialiseChildren;
                        return true;
                    };

                return property;
            }

        }
    }
}
