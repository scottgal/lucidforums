namespace LucidForums.Models.ViewModels.Controls
{
    public class TypeaheadModel
    {
        /// <summary>
        /// API endpoint used to fetch typeahead results.
        /// </summary>
        public string? SearchEndpoint { get; set; }

        /// <summary>
        /// A unique element id used to scope DOM elements for the control.
        /// </summary>
        public string? SearchElementId { get; set; }

        /// <summary>
        /// HTMX endpoint to invoke when user confirms a result (optional).
        /// </summary>
        public string? HtmxEndpoint { get; set; }

        /// <summary>
        /// HTMX target selector or id for where to swap returned content (optional).
        /// </summary>
        public string? HtmxTarget { get; set; }

        /// <summary>
        /// When set, selecting a result will redirect to this URL if result has no specific URL.
        /// </summary>
        public string? RedirectUrl { get; set; }

        /// <summary>
        /// Placeholder text for the search input.
        /// </summary>
        public string? PlaceHolder { get; set; }

        /// <summary>
        /// Initial input value.
        /// </summary>
        public string? Value { get; set; }
    }
}