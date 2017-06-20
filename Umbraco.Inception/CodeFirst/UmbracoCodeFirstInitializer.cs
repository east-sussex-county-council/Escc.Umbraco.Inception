using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Hosting;
using Umbraco.Core;
using Umbraco.Core.Models;
using Umbraco.Core.Services;
using Umbraco.Inception.Attributes;
using Umbraco.Inception.BL;
using Umbraco.Inception.Extensions;

namespace Umbraco.Inception.CodeFirst
{
    public static class UmbracoCodeFirstInitializer
    {
        /// <summary>
        /// This method will create or update the Content Type in Umbraco.
        /// It's possible that you need to run this method a few times to create all relations between Content Types.
        /// </summary>
        /// <param name="type">The type of your model that contains an UmbracoContentTypeAttribute</param>
        public static void CreateOrUpdateEntity(Type type)
        {
            var contentTypeService = ApplicationContext.Current.Services.ContentTypeService;
            var fileService = ApplicationContext.Current.Services.FileService;
            var dataTypeService = ApplicationContext.Current.Services.DataTypeService;

            var contentTypeAttribute = type.GetCustomAttribute<UmbracoContentTypeAttribute>();
            if (contentTypeAttribute != null)
            {
                if (!contentTypeService.GetAllContentTypes().Any(x => x != null && x.Alias == contentTypeAttribute.ContentTypeAlias))
                {
                    CreateContentType(contentTypeService, fileService, contentTypeAttribute, type, dataTypeService);
                }
                else
                {
                    //update
                    IContentType contentType = contentTypeService.GetContentType(contentTypeAttribute.ContentTypeAlias);
                    UpdateContentType(contentTypeService, fileService, contentTypeAttribute, contentType, type, dataTypeService);
                }
                return;
            }

            var mediaTypeAttribute = type.GetCustomAttribute<UmbracoMediaTypeAttribute>();
            if (mediaTypeAttribute != null)
            {
                if (!contentTypeService.GetAllMediaTypes().Any(x => x != null && x.Alias == mediaTypeAttribute.MediaTypeAlias))
                {
                    CreateMediaType(contentTypeService, fileService, mediaTypeAttribute, type, dataTypeService);
                }
                else
                {
                    //update
                    IMediaType mediaType = contentTypeService.GetMediaType(mediaTypeAttribute.MediaTypeAlias);
                    UpdateMediaType(contentTypeService, fileService, mediaTypeAttribute, mediaType, type, dataTypeService);
                }
                return;
            }

            var dataTypeAttribute = type.GetCustomAttribute<UmbracoDataTypeAttribute>();
            if (dataTypeAttribute != null)
            {
                CreateDataType(type);
                return;
            }
        }

        #region Create content types

        /// <summary>
        /// This method is called when the Content Type declared in the attribute hasn't been found in Umbraco
        /// </summary>
        /// <param name="contentTypeService"></param>
        /// <param name="fileService"></param>
        /// <param name="attribute"></param>
        /// <param name="type"></param>
        /// <param name="dataTypeService"></param>
        private static void CreateContentType(IContentTypeService contentTypeService, IFileService fileService,
            UmbracoContentTypeAttribute attribute, Type type, IDataTypeService dataTypeService)
        {
            IContentType newContentType;
            Type parentType = type.BaseType;
            if (parentType != null && parentType != typeof(UmbracoGeneratedBase) && parentType.GetBaseTypes(false).Any(x => x == typeof(UmbracoGeneratedBase)))
            {
                UmbracoContentTypeAttribute parentAttribute = parentType.GetCustomAttribute<UmbracoContentTypeAttribute>();
                if (parentAttribute != null)
                {
                    string parentAlias = parentAttribute.ContentTypeAlias;
                    IContentType parentContentType = contentTypeService.GetContentType(parentAlias);
                    newContentType = new ContentType(parentContentType);
                }
                else
                {
                    throw new Exception("The given base class has no UmbracoContentTypeAttribute");
                }
            }
            else
            {
                newContentType = new ContentType(-1);
            }

            newContentType.Name = attribute.ContentTypeName;
            newContentType.Alias = attribute.ContentTypeAlias;
            newContentType.Icon = attribute.Icon;
            newContentType.Description = attribute.Description;

            if (attribute.CreateMatchingView)
            {
                SetDefaultTemplateAndCreateIfNotExists(fileService, attribute.MasterTemplate, attribute.TemplateLocation, type, newContentType);
            }

            CreateAdditionalTemplates(newContentType, type, fileService, attribute.MasterTemplate, attribute.TemplateLocation);

            newContentType.AllowedAsRoot = attribute.AllowedAtRoot;
            newContentType.IsContainer = attribute.EnableListView;
            newContentType.AllowedContentTypes = FetchAllowedContentTypes(attribute.AllowedChildren, contentTypeService);

            //create tabs 
            CreateTabs(newContentType, type, dataTypeService);

            //create properties on the generic tab
            var propertiesOfRoot = type.GetProperties().Where(x => x.GetCustomAttribute<UmbracoPropertyAttribute>() != null);
            foreach (var item in propertiesOfRoot)
            {
                CreateProperty(newContentType, null, dataTypeService, true, item);
            }

            //Save and persist the content Type
            contentTypeService.Save(newContentType, 0);
        }

        /// <summary>
        /// Creates a View if specified in the attribute
        /// </summary>
        /// <param name="fileService">The file service.</param>
        /// <param name="masterTemplate">The parent master page or MVC layout.</param>
        /// <param name="templateLocation">The template location.</param>
        /// <param name="type">The type.</param>
        /// <param name="contentType">Type of the content.</param>
        private static void SetDefaultTemplateAndCreateIfNotExists(IFileService fileService, string masterTemplate, string templateLocation, Type type, IContentType contentType)
        {
            var templateAlias = String.IsNullOrEmpty(masterTemplate) ? contentType.Alias : masterTemplate;
            var currentTemplate = fileService.GetTemplate(templateAlias) as Template;
            if (currentTemplate == null)
            {
                currentTemplate = CreateTemplateIfNotExists(fileService, contentType.Name, contentType.Alias, masterTemplate, type, templateLocation);
            }

            AddToAllowedTemplates(currentTemplate, contentType);
            contentType.SetDefaultTemplate(currentTemplate);

            //TODO: in Umbraco 7.1 it will be possible to set the master template of the newly created template
            //https://github.com/umbraco/Umbraco-CMS/pull/294
        }

        private static void AddToAllowedTemplates(Template template, IContentType documentType)
        {
            var allowedTemplates = new List<ITemplate>(documentType.AllowedTemplates);

            var alreadyAllowed = false;
            foreach (var allowedTemplate in allowedTemplates)
            {
                //  Use .Id because Umbraco 7.4.x throws an exception accessing .Alias due to an internal cast 
                if (allowedTemplate.Id == template.Id)
                {
                    alreadyAllowed = true;
                    break;
                }
            }

            if (!alreadyAllowed)
            {
                allowedTemplates.Add(template);
                documentType.AllowedTemplates = allowedTemplates;
            }
        }


        /// <summary>
        /// Scans for properties on the model which have the UmbracoTab attribute
        /// </summary>
        /// <param name="contentType">Type of the content.</param>
        /// <param name="model">The model.</param>
        /// <param name="fileService">The file service.</param>
        /// <param name="masterTemplate">The master template.</param>
        /// <param name="customDirectoryPath">The custom directory path.</param>
        private static void CreateAdditionalTemplates(IContentType contentType, Type model, IFileService fileService, string masterTemplate = null, string customDirectoryPath = null)
        {
            var properties = model.GetProperties().Where(x => x.DeclaringType == model && x.GetCustomAttribute<UmbracoTemplateAttribute>() != null).ToArray();
            int length = properties.Length;

            for (int i = 0; i < length; i++)
            {
                var templateAttribute = properties[i].GetCustomAttribute<UmbracoTemplateAttribute>();
                var template = CreateTemplateIfNotExists(fileService, templateAttribute.DisplayName, templateAttribute.Alias, masterTemplate, model, customDirectoryPath);
                AddToAllowedTemplates(template, contentType);
            }
        }

        /// <summary>
        /// Creates an Umbraco template and associated view file.
        /// </summary>
        /// <param name="fileService">The file service.</param>
        /// <param name="displayName">The display name of the Umbraco template.</param>
        /// <param name="templateAlias">Alias of the template, also used as the name of the view file.</param>
        /// <param name="masterTemplate">The master page or MVC to inherit from.</param>
        /// <param name="type">An Inception type name used in the generated view file.</param>
        /// <param name="customDirectoryPath">The custom directory path if not ~/Views/.</param>
        /// <returns></returns>
        private static Template CreateTemplateIfNotExists(IFileService fileService, string displayName, string templateAlias, string masterTemplate=null, Type type=null, string customDirectoryPath=null)
        {
            var template = fileService.GetTemplate(templateAlias) as Template;
            if (template == null)
            {
                var defaultDirectoryPath = "~/Views/";
                var defaultFilePath = string.Format(CultureInfo.InvariantCulture, "{0}{1}.cshtml", defaultDirectoryPath, templateAlias);

                string filePath;
                if (string.IsNullOrEmpty(customDirectoryPath))
                {
                    filePath = defaultFilePath;
                }
                else
                {
                    filePath = string.Format(CultureInfo.InvariantCulture, "{0}{1}{2}.cshtml",
                        customDirectoryPath, // The template location
                        customDirectoryPath.EndsWith("/") ? string.Empty : "/", // Ensure the template location ends with a "/"
                        templateAlias); // The alias
                }

                string physicalViewFileLocation = HostingEnvironment.MapPath(filePath);
                if (System.IO.File.Exists(physicalViewFileLocation))
                {
                    // If we create the template record in the database Umbraco will create a view file regardless, overwriting what's there. 
                    // If the MVC view is already there we can protect it by making a temporary copy which can then be moved back to the original location. 
                    System.IO.File.Copy(physicalViewFileLocation, physicalViewFileLocation + ".temp");

                    template = new Template(defaultDirectoryPath, displayName, templateAlias);
                    fileService.SaveTemplate(template, 0);

                    if (System.IO.File.Exists(physicalViewFileLocation) && System.IO.File.Exists(physicalViewFileLocation + ".temp"))
                    {
                        System.IO.File.Delete(physicalViewFileLocation);
                        System.IO.File.Move(physicalViewFileLocation + ".temp", physicalViewFileLocation);
                    }
                }
                else
                {
                    template = new Template(filePath, displayName, templateAlias);
                    CreateViewFile(masterTemplate, template, type, fileService);
                    fileService.SaveTemplate(template, 0);
                }
            }
            return template;
        }

        /// <summary>
        /// Scans for properties on the model which have the UmbracoTab attribute
        /// </summary>
        /// <param name="newContentType"></param>
        /// <param name="model"></param>
        /// <param name="dataTypeService"></param>
        private static void CreateTabs(IContentTypeBase newContentType, Type model, IDataTypeService dataTypeService)
        {
            var properties = model.GetProperties().Where(x => x.DeclaringType == model && x.GetCustomAttribute<UmbracoTabAttribute>() != null).ToArray();
            int length = properties.Length;

            for (int i = 0; i < length; i++)
            {
                var tabAttribute = properties[i].GetCustomAttribute<UmbracoTabAttribute>();

                newContentType.AddPropertyGroup(tabAttribute.Name);
                newContentType.PropertyGroups.Where(x => x.Name == tabAttribute.Name).FirstOrDefault().SortOrder = tabAttribute.SortOrder;

                CreateProperties(properties[i], newContentType, tabAttribute.Name, dataTypeService);
            }
        }

        /// <summary>
        /// Every property on the Tab object is scanned for the UmbracoProperty attribute
        /// </summary>
        /// <param name="propertyInfo"></param>
        /// <param name="newContentType"></param>
        /// <param name="tabName"></param>
        /// <param name="dataTypeService"></param>
        /// <param name="atTabGeneric"></param>
        private static void CreateProperties(PropertyInfo propertyInfo, IContentTypeBase newContentType, string tabName, IDataTypeService dataTypeService, bool atTabGeneric = false)
        {
            //type is from TabBase
            Type type = propertyInfo.PropertyType;
            var properties = type.GetProperties().Where(x => x.GetCustomAttribute<UmbracoPropertyAttribute>() != null);
            if (properties.Count() > 0)
            {
                foreach (var item in properties)
                {
                    CreateProperty(newContentType, tabName, dataTypeService, atTabGeneric, item);
                }
            }
        }

        /// <summary>
        /// Creates a new property on the ContentType under the correct tab
        /// </summary>
        /// <param name="newContentType"></param>
        /// <param name="tabName"></param>
        /// <param name="dataTypeService"></param>
        /// <param name="atTabGeneric"></param>
        /// <param name="item"></param>
        private static void CreateProperty(IContentTypeBase newContentType, string tabName, IDataTypeService dataTypeService, bool atTabGeneric, PropertyInfo item)
        {
            UmbracoPropertyAttribute attribute = item.GetCustomAttribute<UmbracoPropertyAttribute>();

            IDataTypeDefinition dataTypeDef;
            if (string.IsNullOrEmpty(attribute.DataTypeInstanceName))
            {
                dataTypeDef = dataTypeService.GetDataTypeDefinitionByPropertyEditorAlias(attribute.DataType).FirstOrDefault();
            }
            else
            {
                dataTypeDef = dataTypeService.GetDataTypeDefinitionByPropertyEditorAlias(attribute.DataType).FirstOrDefault(x => x.Name == attribute.DataTypeInstanceName);
            }

            if (dataTypeDef != null)
            {
                PropertyType propertyType = new PropertyType(dataTypeDef);
                propertyType.Name = attribute.Name;
                propertyType.Alias = ((atTabGeneric || !attribute.AddTabAliasToPropertyAlias) ? attribute.Alias : UmbracoCodeFirstExtensions.HyphenToUnderscore(UmbracoCodeFirstExtensions.ParseUrl(attribute.Alias + "_" + tabName, false)));
                propertyType.Description = attribute.Description;
                propertyType.Mandatory = attribute.Mandatory;
                propertyType.SortOrder = attribute.SortOrder;
                propertyType.ValidationRegExp = attribute.ValidationRegularExpression;

                if (atTabGeneric)
                {
                    newContentType.AddPropertyType(propertyType);
                }
                else
                {
                    newContentType.AddPropertyType(propertyType, tabName);
                }
            }
        }

        /// <summary>
        /// Creates a new dataType
        /// </summary>
        /// <param name="type">The type of your model that contains the prevalues for the custom data type.</param>
        public static void CreateDataType(Type type)
        {                      
            UmbracoDataTypeAttribute dataTypeAttribute = type.GetCustomAttribute<UmbracoDataTypeAttribute>();
            if (dataTypeAttribute == null)
            {
                return;
            }
            else
            {
                var dataTypeService = ApplicationContext.Current.Services.DataTypeService;

                List<IDataTypeDefinition> dataTypeDefinitions = dataTypeService.GetDataTypeDefinitionByPropertyEditorAlias(dataTypeAttribute.PropertyEditorAlias).ToList(); // Obtains all the property editor alias names.

                var matchingDataTypeDefinition = dataTypeDefinitions.SingleOrDefault(x => x.Name == dataTypeAttribute.DataTypeName); // Checks to see if the data type name exist.

                /* If not, create a new datatype based on the property editor.
                 * Do not try to update an existing data type. For the data type itself, Umbraco throws a DuplicateNameException. 
                 * Calling fileService.SavePreValues works, but wipes out all the content data even for existing prevalues. Too dangerous to use.
                 */
                if (matchingDataTypeDefinition == null)
                {
                    matchingDataTypeDefinition = new DataTypeDefinition(-1, dataTypeAttribute.PropertyEditorAlias);
                    matchingDataTypeDefinition.Name = dataTypeAttribute.DataTypeName;
                    matchingDataTypeDefinition.DatabaseType = dataTypeAttribute.DatabaseType;
                    dataTypeService.Save(matchingDataTypeDefinition);

                    if (dataTypeAttribute.PreValues != null)
                    {
                        var preValueProviderType = dataTypeAttribute.PreValues;
                        var preValueProviderInstance = Activator.CreateInstance(preValueProviderType);
                        var preValues = ((IPreValueProvider) preValueProviderInstance).PreValues;

                        dataTypeService.SaveDataTypeAndPreValues(matchingDataTypeDefinition, preValues);
                    }
                }
            }
        }

        #endregion Create content types

        #region Create media types

        /// <summary>
        /// This method is called when the Media Type declared in the attribute hasn't been found in Umbraco
        /// </summary>
        /// <param name="contentTypeService"></param>
        /// <param name="fileService"></param>
        /// <param name="attribute"></param>
        /// <param name="type"></param>
        /// <param name="dataTypeService"></param>
        private static void CreateMediaType(IContentTypeService contentTypeService, IFileService fileService,
            UmbracoMediaTypeAttribute attribute, Type type, IDataTypeService dataTypeService)
        {
            IMediaType newMediaType;
            Type parentType = type.BaseType;
            if (parentType != null && parentType != typeof(UmbracoGeneratedBase) && parentType.GetBaseTypes(false).Any(x => x == typeof(UmbracoGeneratedBase)))
            {
                UmbracoMediaTypeAttribute parentAttribute = parentType.GetCustomAttribute<UmbracoMediaTypeAttribute>();
                if (parentAttribute != null)
                {
                    string parentAlias = parentAttribute.MediaTypeAlias;
                    IMediaType parentContentType = contentTypeService.GetMediaType(parentAlias);
                    newMediaType = new MediaType(parentContentType);
                }
                else
                {
                    throw new Exception("The given base class has no UmbracoMediaTypeAttribute");
                }
            }
            else
            {
                newMediaType = new MediaType(-1);
            }

            newMediaType.Name = attribute.MediaTypeName;
            newMediaType.Alias = attribute.MediaTypeAlias;
            newMediaType.Icon = attribute.Icon;
            newMediaType.Description = attribute.Description;
            newMediaType.AllowedAsRoot = attribute.AllowedAtRoot;
            newMediaType.IsContainer = attribute.EnableListView;
            newMediaType.AllowedContentTypes = FetchAllowedContentTypes(attribute.AllowedChildren, contentTypeService);

            //create tabs
            CreateTabs(newMediaType, type, dataTypeService);
            
            //create properties on the generic tab
            var propertiesOfRoot = type.GetProperties().Where(x => x.GetCustomAttribute<UmbracoPropertyAttribute>() != null);
            foreach (var item in propertiesOfRoot)
            {
                CreateProperty(newMediaType, null, dataTypeService, true, item);
            }

            //Save and persist the media Type
            contentTypeService.Save(newMediaType, 0);
        }

        #endregion

        #region Update content types

        /// <summary>
        /// Update the existing content Type based on the data in the attributes
        /// </summary>
        /// <param name="contentTypeService"></param>
        /// <param name="fileService"></param>
        /// <param name="attribute"></param>
        /// <param name="contentType"></param>
        /// <param name="type"></param>
        /// <param name="dataTypeService"></param>
        private static void UpdateContentType(IContentTypeService contentTypeService, IFileService fileService, UmbracoContentTypeAttribute attribute, IContentType contentType, Type type, IDataTypeService dataTypeService)
        {
            contentType.Name = attribute.ContentTypeName;
            contentType.Alias = attribute.ContentTypeAlias;
            contentType.Icon = attribute.Icon;
            contentType.Description = attribute.Description;
            contentType.IsContainer = attribute.EnableListView;
            contentType.AllowedContentTypes = FetchAllowedContentTypes(attribute.AllowedChildren, contentTypeService);
            contentType.AllowedAsRoot = attribute.AllowedAtRoot;

            Type parentType = type.BaseType;
            if (parentType != null && parentType != typeof(UmbracoGeneratedBase) && parentType.GetBaseTypes(false).Any(x => x == typeof(UmbracoGeneratedBase)))
            {
                UmbracoContentTypeAttribute parentAttribute = parentType.GetCustomAttribute<UmbracoContentTypeAttribute>();
                if (parentAttribute != null)
                {
                    string parentAlias = parentAttribute.ContentTypeAlias;
                    IContentType parentContentType = contentTypeService.GetContentType(parentAlias);
                    contentType.ParentId = parentContentType.Id;
                }
                else
                {
                    throw new Exception("The given base class has no UmbracoContentTypeAttribute");
                }
            }

            if (attribute.CreateMatchingView)
            {
                SetDefaultTemplateAndCreateIfNotExists(fileService, attribute.MasterTemplate, attribute.TemplateLocation, type, contentType);

                //Template currentTemplate = fileService.GetTemplate(attribute.ContentTypeAlias) as Template;
                //if (currentTemplate == null)
                //{
                //    //there should be a template but there isn't so we create one
                //    currentTemplate = new Template("~/Views/" + attribute.ContentTypeAlias + ".cshtml", attribute.ContentTypeName, attribute.ContentTypeAlias);
                //    CreateViewFile(attribute.ContentTypeAlias, attribute.MasterTemplate, currentTemplate, type, fileService);
                //    fileService.SaveTemplate(currentTemplate, 0);
                //}
                //documentType.AllowedTemplates = new ITemplate[] { currentTemplate };
                //documentType.SetDefaultTemplate(currentTemplate);
            }

            CreateAdditionalTemplates(contentType, type, fileService);

            VerifyProperties(contentType, type, dataTypeService);

            //verify if a tab has no properties, if so remove
            var propertyGroups = contentType.PropertyGroups.ToArray();
            int length = propertyGroups.Length;
            for (int i = 0; i < length; i++)
            {
                if (propertyGroups[i].PropertyTypes.Count == 0)
                {
                    //remove
                    contentType.RemovePropertyGroup(propertyGroups[i].Name);
                }
            }

            //persist
            contentTypeService.Save(contentType, 0);
        }

        /// <summary>
        /// Loop through all properties and remove existing ones if necessary
        /// </summary>
        /// <param name="contentType"></param>
        /// <param name="type"></param>
        /// <param name="dataTypeService"></param>
        private static void VerifyProperties(IContentTypeBase contentType, Type type, IDataTypeService dataTypeService)
        {
            var properties = type.GetProperties().Where(x => x.GetCustomAttribute<UmbracoTabAttribute>() != null).ToArray();
            List<string> propertiesThatShouldExist = new List<string>();

            foreach (var propertyTab in properties)
            {
                var tabAttribute = propertyTab.GetCustomAttribute<UmbracoTabAttribute>();
                if (!contentType.PropertyGroups.Any(x => x.Name == tabAttribute.Name))
                {
                    contentType.AddPropertyGroup(tabAttribute.Name);
                }

                propertiesThatShouldExist.AddRange(VerifyAllPropertiesOnTab(propertyTab, contentType, tabAttribute.Name, dataTypeService));
            }

            var propertiesOfRoot = type.GetProperties().Where(x => x.GetCustomAttribute<UmbracoPropertyAttribute>() != null);
            foreach (var item in propertiesOfRoot)
            {
                //TODO: check for correct name
                propertiesThatShouldExist.Add(VerifyExistingProperty(contentType, null, dataTypeService, item, true));
            }

            //loop through all the properties on the ContentType to see if they should be removed;
            var existingUmbracoProperties = contentType.PropertyTypes.ToArray();
            int length = contentType.PropertyTypes.Count();
            for (int i = 0; i < length; i++)
            {
                if (!propertiesThatShouldExist.Contains(existingUmbracoProperties[i].Alias))
                {
                    //remove the property
                    contentType.RemovePropertyType(existingUmbracoProperties[i].Alias);
                }
            }
        }

        /// <summary>
        /// Scan the properties on tabs
        /// </summary>
        /// <param name="propertyTab"></param>
        /// <param name="contentType"></param>
        /// <param name="tabName"></param>
        /// <param name="dataTypeService"></param>
        /// <returns></returns>
        private static IEnumerable<string> VerifyAllPropertiesOnTab(PropertyInfo propertyTab, IContentTypeBase contentType, string tabName, IDataTypeService dataTypeService)
        {
            Type type = propertyTab.PropertyType;
            var properties = type.GetProperties().Where(x => x.GetCustomAttribute<UmbracoPropertyAttribute>() != null);
            if (properties.Count() > 0)
            {
                List<string> propertyAliases = new List<string>();
                foreach (var item in properties)
                {
                    propertyAliases.Add(VerifyExistingProperty(contentType, tabName, dataTypeService, item));
                }
                return propertyAliases;
            }
            return new string[0];
        }

        private static string VerifyExistingProperty(IContentTypeBase contentType, string tabName, IDataTypeService dataTypeService, PropertyInfo item, bool atGenericTab = false)
        {
            UmbracoPropertyAttribute attribute = item.GetCustomAttribute<UmbracoPropertyAttribute>();
            IDataTypeDefinition dataTypeDef;
            if (string.IsNullOrEmpty(attribute.DataTypeInstanceName))
            {
                dataTypeDef = dataTypeService.GetDataTypeDefinitionByPropertyEditorAlias(attribute.DataType).FirstOrDefault();
            }
            else
            {
                dataTypeDef = dataTypeService.GetDataTypeDefinitionByPropertyEditorAlias(attribute.DataType).FirstOrDefault(x => x.Name == attribute.DataTypeInstanceName);
            }

            if (dataTypeDef != null)
            {
                PropertyType property;
                bool alreadyExisted = contentType.PropertyTypeExists(attribute.Alias);
                // TODO: Added attribute.Tab != null after Generic Properties add, is this bulletproof?
                if (alreadyExisted && attribute.Tab != null)
                {
                    property = contentType.PropertyTypes.FirstOrDefault(x => x.Alias == attribute.Alias);
                }
                else
                {
                    property = new PropertyType(dataTypeDef);
                }

                property.Name = attribute.Name;
                //TODO: correct name?
                property.Alias = ((atGenericTab || !attribute.AddTabAliasToPropertyAlias) ? attribute.Alias : UmbracoCodeFirstExtensions.HyphenToUnderscore(UmbracoCodeFirstExtensions.ParseUrl(attribute.Alias + "_" + tabName, false)));
                property.Description = attribute.Description;
                property.Mandatory = attribute.Mandatory;

                if (!alreadyExisted)
                {
                    if (atGenericTab)
                    {
                        contentType.AddPropertyType(property);
                    }
                    else
                    {
                        contentType.AddPropertyType(property, tabName);
                    }
                }

                return property.Alias;
            }
            return null;
        }

        #endregion Update content types

        #region Update media types

        /// <summary>
        /// Update the existing content Type based on the data in the attributes
        /// </summary>
        /// <param name="contentTypeService"></param>
        /// <param name="fileService"></param>
        /// <param name="attribute"></param>
        /// <param name="mediaType"></param>
        /// <param name="type"></param>
        /// <param name="dataTypeService"></param>
        private static void UpdateMediaType(IContentTypeService contentTypeService, IFileService fileService, UmbracoMediaTypeAttribute attribute, IMediaType mediaType, Type type, IDataTypeService dataTypeService)
        {
            mediaType.Name = attribute.MediaTypeName;
            mediaType.Alias = attribute.MediaTypeAlias;
            mediaType.Icon = attribute.Icon;
            mediaType.Description = attribute.Description;
            mediaType.IsContainer = attribute.EnableListView;
            mediaType.AllowedContentTypes = FetchAllowedContentTypes(attribute.AllowedChildren, contentTypeService);
            mediaType.AllowedAsRoot = attribute.AllowedAtRoot;

            Type parentType = type.BaseType;
            if (parentType != null && parentType != typeof(UmbracoGeneratedBase) && parentType.GetBaseTypes(false).Any(x => x == typeof(UmbracoGeneratedBase)))
            {
                UmbracoMediaTypeAttribute parentAttribute = parentType.GetCustomAttribute<UmbracoMediaTypeAttribute>();
                if (parentAttribute != null)
                {
                    string parentAlias = parentAttribute.MediaTypeAlias;
                    IMediaType parentContentType = contentTypeService.GetMediaType(parentAlias);
                    mediaType.ParentId = parentContentType.Id;
                }
                else
                {
                    throw new Exception("The given base class has no UmbracoMediaTypeAttribute");
                }
            }

            VerifyProperties(mediaType, type, dataTypeService);

            //verify if a tab has no properties, if so remove
            var propertyGroups = mediaType.PropertyGroups.ToArray();
            int length = propertyGroups.Length;
            for (int i = 0; i < length; i++)
            {
                if (propertyGroups[i].PropertyTypes.Count == 0)
                {
                    //remove
                    mediaType.RemovePropertyGroup(propertyGroups[i].Name);
                }
            }

            //persist
            contentTypeService.Save(mediaType, 0);
        }

        #endregion

        #region Shared logic

        /// <summary>
        /// Gets the allowed children
        /// </summary>
        /// <param name="types"></param>
        /// <param name="contentTypeService"></param>
        /// <returns></returns>
        private static IEnumerable<ContentTypeSort> FetchAllowedContentTypes(Type[] types, IContentTypeService contentTypeService)
        {
            if (types == null) return new ContentTypeSort[0];

            List<ContentTypeSort> contentTypeSorts = new List<ContentTypeSort>();

            List<string> aliases = GetAliasesFromTypes(types);

            var contentTypes = contentTypeService.GetAllContentTypes().Where(x => aliases.Contains(x.Alias)).ToArray();

            int length = contentTypes.Length;
            for (int i = 0; i < length; i++)
            {
                ContentTypeSort sort = new ContentTypeSort();
                sort.Alias = contentTypes[i].Alias;
                int id = contentTypes[i].Id;
                sort.Id = new Lazy<int>(() => { return id; });
                sort.SortOrder = i;
                contentTypeSorts.Add(sort);
            }
            return contentTypeSorts;
        }

        private static List<string> GetAliasesFromTypes(Type[] types)
        {
            List<string> aliases = new List<string>();

            foreach (Type type in types)
            {
                UmbracoContentTypeAttribute attribute = type.GetCustomAttribute<UmbracoContentTypeAttribute>();
                if (attribute != null)
                {
                    aliases.Add(attribute.ContentTypeAlias);
                }
            }

            return aliases;
        }

        private static void CreateViewFile(string masterTemplate, Template template, Type type, IFileService fileService)
        {
            string physicalViewFileLocation = HostingEnvironment.MapPath(template.Path);
            if (string.IsNullOrEmpty(physicalViewFileLocation))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Failed to {0} to a physical location", template.Path));
            }

            var templateContent = CreateDefaultTemplateContent(masterTemplate, type);
            template.Content = templateContent;

            using (var sw = System.IO.File.CreateText(physicalViewFileLocation))
            {
                sw.Write(templateContent);
            }
        }

        private static string CreateDefaultTemplateContent(string master, Type type)
        {
            var sb = new StringBuilder();
            sb.AppendLine("@inherits Umbraco.Web.Mvc.UmbracoTemplatePage");
            sb.AppendLine("@*@using Qite.Umbraco.CodeFirst.Extensions;*@");
            sb.AppendLine("@{");
            sb.AppendLine("\tLayout = \"" + master + ".cshtml\";");
            if (type != null)
            {
                sb.AppendLine("\t//" + type.Name + " model = Model.Content.ConvertToRealModel<" + type.Name + ">();");
            }
            sb.AppendLine("}");

            return sb.ToString();
        }

        #endregion Shared logic
    }
}