﻿using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Smartstore.Web.TagHelpers.Admin
{
    [OutputElementHint("input")]
    [HtmlTargetElement(EditorTagName, Attributes = ForAttributeName, TagStructure = TagStructure.WithoutEndTag)]
    public class EditorTagHelper : BaseFormTagHelper
    {
        const string EditorTagName = "editor";
        const string TemplateAttributeName = "asp-template";
        const string ValueAttributeName = "asp-value";
        const string DisabledAttributeName = "sm-disabled";
        const string PostfixAttributeName = "sm-postfix";
        const string PlaceholderAttributeName = "placeholder";
        const string AutocompleteAttributeName = "autocomplete";

        /// <summary>
        /// Specifies the editor template which will be used to render the field.
        /// </summary>
        [HtmlAttributeName(TemplateAttributeName)]
        public string Template { get; set; }

        /// <summary>
        /// Specifies the value to set into editor input tag.
        /// </summary>
        [HtmlAttributeName(ValueAttributeName)]
        public string Value { get; set; }

        /// <summary>
        /// Specifies whether the editor field should be displayed disabled.
        /// </summary>
        [HtmlAttributeName(DisabledAttributeName)]
        public bool IsDisabled { get; set; } = false;

        /// <summary>
        /// The text which will be displayed inside the input tag as a post fix.
        /// </summary>
        [HtmlAttributeName(PostfixAttributeName)]
        public string Postfix { get; set; }

        /// <summary>
        /// Defines the placeholder for the editor input tag.
        /// </summary>
        [HtmlAttributeName(PlaceholderAttributeName)]
        public string Placeholder { get; set; }

        /// <summary>
        /// Defines whether the input tag will get the autocomplete attribute.
        /// </summary>
        [HtmlAttributeName(AutocompleteAttributeName)]
        public bool Autocomplete { get; set; } = false;

        private readonly IHtmlHelper _htmlHelper;

        public EditorTagHelper(IHtmlHelper htmlHelper)
        {
            _htmlHelper = htmlHelper;
        }

        protected override void ProcessCore(TagHelperContext context, TagHelperOutput output)
        {
            output.SuppressOutput();
            
            var htmlAttributes = new Dictionary<string, object>();

            if (Value.HasValue())
                htmlAttributes.Add("value", Value);

            if (Placeholder.HasValue())
                htmlAttributes.Add("placeholder", Placeholder);

            if (Autocomplete)
                htmlAttributes.Add("autocomplete", Autocomplete);

            if (IsDisabled)
                htmlAttributes.Add("disabled", "disabled");

            if (Postfix.HasValue())
                htmlAttributes.Add("postfix", Placeholder);

            var viewContextAware = _htmlHelper as IViewContextAware;
            viewContextAware?.Contextualize(ViewContext);

            var attrs = context.AllAttributes.Where(x => x.Name.EqualsNoCase("attrs")).FirstOrDefault();
            if (attrs != null && attrs.Value != null)
            {
                foreach (var attr in attrs.Value as AttributeDictionary)
                {
                    htmlAttributes.Add(attr.Key, attr.Value);
                }
            }

            if (_htmlHelper is HtmlHelper helper)
            {
                var method = helper.GetType().GetMethod("GenerateEditor", BindingFlags.NonPublic | BindingFlags.Instance);
                var content = (IHtmlContent)method.Invoke(helper, new object[] { For.ModelExplorer, For.Name, Template, htmlAttributes });
                output.Content.SetHtmlContent(content);
            }
            else
            {
                var htmlOutput = _htmlHelper.Editor(For.Name, Template, new { htmlAttributes, postfix = Postfix });
                output.Content.SetHtmlContent(htmlOutput);
            }
        }
    }
}
