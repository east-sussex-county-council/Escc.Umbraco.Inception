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

        /// <summary>
        /// Put this on the class that represents your data type you want to create and insert into the database.
        /// </summary>
        /// <param name="dataTypeName">Friendly name of the data type</param>
        /// <param name="propertyEditorAlias">Alias of the data type</param>
        /// <param name="preValues">Initial values</param>      
        public UmbracoCustomDataTypeAttribute(string dataTypeName, string propertyEditorAlias, Type preValues)
        //public UmbracoCustomDataTypeAttribute(string dataTypeName, string propertyEditorAlias, Type preValues)
        {
            DataTypeName = dataTypeName;
            PropertyEditorAlias = propertyEditorAlias;
            //PreValues = preValues;
            PreValues = preValues;
        }
    }
}
