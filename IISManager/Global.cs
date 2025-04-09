namespace Techie.IISManager
{
    using System;
    using System.IO;
    using System.Xml;
    using System.Xml.Serialization;
    using log4net;

    /// <summary>
    /// 
    /// </summary>
    public class Global
    {
        /// <summary>
        /// 
        /// </summary>
        public static string DataFolder { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        public static ILog Log { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public static string SitesFolder { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        public static string ServerFqdn { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        static Global()
        {
            Log = LogManager.GetLogger(typeof(Global));

            DataFolder = $"{AppDomain.CurrentDomain.BaseDirectory}\\Configuration";

            if (!Directory.Exists(DataFolder))
            {
                Log.InfoFormat("Creating data folder {0}", DataFolder);
                Directory.CreateDirectory(DataFolder);
            }

            SitesFolder = ConfigurationManager.AppSetting["IISManager:SitesFolder"]!;
            ServerFqdn = ConfigurationManager.AppSetting["IISManager:ServerFqdn"]!;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="inputFile"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public static object? DeserializeObject(string inputFile, Type type)
        {
            XmlSerializer serializer = new XmlSerializer(type);
            object? result;

            using (FileStream plainTextFile = new FileStream(inputFile, FileMode.Open, FileAccess.Read))
            {
                var reader = XmlReader.Create(plainTextFile, new XmlReaderSettings() { CheckCharacters = false });
                result = serializer.Deserialize(reader);
                reader.Close();
            }

            return result;
        }
    }
}
