namespace LucidForums.Models.ViewModels.Controls
{
    public class DateTimePickerModel
    {
        /// <summary>
        /// Placeholder text for the display input.
        /// </summary>
        public string? Placeholder { get; set; }

        /// <summary>
        /// Name/id used for the hidden input field that carries the ISO value.
        /// </summary>
        public string? ElementId { get; set; }

        /// <summary>
        /// Pre-selected date/time in UTC or local (view converts to ISO).
        /// </summary>
        public DateTime? SelectedDateTime { get; set; }
    }
}