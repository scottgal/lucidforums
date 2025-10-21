namespace LucidForums.Models.ViewModels.Controls
{
    public class SearchElementModel
    {
        /// <summary>
        /// The DOM id for the wrapping element.
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// Placeholder text shown in the input.
        /// </summary>
        public string? Placeholder { get; set; }

        /// <summary>
        /// MVC controller name targeted by HTMX.
        /// </summary>
        public string? Controller { get; set; }

        /// <summary>
        /// MVC action name targeted by HTMX.
        /// </summary>
        public string? Action { get; set; }

        /// <summary>
        /// Name of the input, submitted as query parameter.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Target element id (without '#') to receive HTMX response.
        /// </summary>
        public string? Target { get; set; }
    }
}