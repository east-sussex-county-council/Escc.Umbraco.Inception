using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Core.Models;
using Umbraco.Core.Services;

namespace Umbraco.Inception.Attributes
{
    [AttributeUsage(AttributeTargets.Class)]
    public class UmbracoCustomDataTypeAttribute : Attribute
    {

        public string DataTypeName { get; set; }
        public string PropertyEditorAlias { get; set; }
        public Type PreValues { get; set; }
        public DataTypeDatabaseType DatabaseType { get; set; }

        /// <summary>
        /// Add on the class that represents your custom data type, so that the custom data type is inserted into the database.
        /// </summary>
        /// <param name="dataTypeName">Friendly name of the data type</param>
        /// <param name="propertyEditorAlias">Alias of the data type</param>
        /// <param name="preValues">Initial values</param>      
        public UmbracoCustomDataTypeAttribute(string dataTypeName, string propertyEditorAlias, Type preValues, DataTypeDatabaseType dataTypeDatabaseType)
        {
            DataTypeName = dataTypeName;
            PropertyEditorAlias = propertyEditorAlias;
            PreValues = preValues;
            DatabaseType = dataTypeDatabaseType;
        }
    }
}
