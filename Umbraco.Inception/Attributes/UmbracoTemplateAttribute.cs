using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Core.Models;
using Umbraco.Core.Services;
using Umbraco.Inception.BL;

namespace Umbraco.Inception.Attributes
{
    /// <summary>
    /// An additional Umbraco template allowed on a content type
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class UmbracoTemplateAttribute : Attribute
    {
        /// <summary>
        /// Gets or sets the template display name.
        /// </summary>
        /// <value>
        /// The display name.
        /// </value>
        public string DisplayName { get; set; }
        
        /// <summary>
        /// Gets or sets the template alias.
        /// </summary>
        /// <value>
        /// The alias.
        /// </value>
        public string Alias { get; set; }
    }
}
