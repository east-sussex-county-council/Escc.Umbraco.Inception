using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Umbraco.Core.Models;

namespace Umbraco.Inception.BL
{
    /// <summary>
    /// A class that provides prevalues when creating an Umbraco datatype
    /// </summary>
    public interface IPreValueProvider
    {
        IDictionary<string, PreValue> PreValues { get; }
    }
}
