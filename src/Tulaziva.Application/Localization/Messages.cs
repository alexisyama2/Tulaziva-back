namespace Tulaziva.Application.Localization;

/// <summary>Keys of the messages. Every key must have a translation in each supported language.</summary>
public static class MessageKeys
{
    public const string ValidationFailed = "Error.ValidationFailed";
    public const string EmailTaken = "Error.EmailTaken";
    public const string InvalidCredentials = "Error.InvalidCredentials";
    public const string UserNotFound = "Error.UserNotFound";
    public const string Unexpected = "Error.Unexpected";
    public const string BadRequest = "Error.BadRequest";

    public const string EmailRequired = "Validation.EmailRequired";
    public const string EmailInvalid = "Validation.EmailInvalid";
    public const string EmailTooLong = "Validation.EmailTooLong";
    public const string PasswordRequired = "Validation.PasswordRequired";
    public const string PasswordTooShort = "Validation.PasswordTooShort";
    public const string PasswordTooLong = "Validation.PasswordTooLong";
    public const string PasswordNeedsLetterAndDigit = "Validation.PasswordNeedsLetterAndDigit";
    public const string DisplayNameRequired = "Validation.DisplayNameRequired";
    public const string DisplayNameTooLong = "Validation.DisplayNameTooLong";
    public const string LanguageUnsupported = "Validation.LanguageUnsupported";
    public const string LatitudeOutOfRange = "Validation.LatitudeOutOfRange";
    public const string LongitudeOutOfRange = "Validation.LongitudeOutOfRange";
    public const string LocationIncomplete = "Validation.LocationIncomplete";
    public const string PageInvalid = "Validation.PageInvalid";
    public const string PageSizeInvalid = "Validation.PageSizeInvalid";

    public const string Forbidden = "Error.Forbidden";
    public const string AdNotFound = "Error.AdNotFound";
    public const string ImageNotFound = "Error.ImageNotFound";
    public const string AdRemoved = "Error.AdRemoved";
    public const string TooManyImages = "Error.TooManyImages";

    public const string CategoryInvalid = "Validation.CategoryInvalid";
    public const string TitleLength = "Validation.TitleLength";
    public const string TitleRequired = "Validation.TitleRequired";
    public const string DescriptionRequired = "Validation.DescriptionRequired";
    public const string DescriptionTooLong = "Validation.DescriptionTooLong";
    public const string PriceInvalid = "Validation.PriceInvalid";
    public const string CurrencyUnsupported = "Validation.CurrencyUnsupported";
    public const string FileRequired = "Validation.FileRequired";
    public const string FileTooLarge = "Validation.FileTooLarge";
    public const string FileExtensionUnsupported = "Validation.FileExtensionUnsupported";
    public const string FileContentInvalid = "Validation.FileContentInvalid";
    public const string SearchTextTooLong = "Validation.SearchTextTooLong";
    public const string PriceRangeInvalid = "Validation.PriceRangeInvalid";
    public const string RadiusInvalid = "Validation.RadiusInvalid";

    public const string PhotoNotFound = "Error.PhotoNotFound";
    public const string ConversationNotFound = "Error.ConversationNotFound";
    public const string AttachmentNotFound = "Error.AttachmentNotFound";
    public const string CannotMessageYourself = "Error.CannotMessageYourself";
    public const string MessageEmpty = "Validation.MessageEmpty";
    public const string MessageTooLong = "Validation.MessageTooLong";
    public const string TooManyFiles = "Validation.TooManyFiles";
    public const string AttachmentExtensionUnsupported = "Validation.AttachmentExtensionUnsupported";
    public const string AttachmentContentInvalid = "Validation.AttachmentContentInvalid";

    public const string AlreadyReported = "Error.AlreadyReported";
    public const string CannotReportOwnAd = "Error.CannotReportOwnAd";
    public const string ReportNotFound = "Error.ReportNotFound";
    public const string ReportAlreadyClosed = "Error.ReportAlreadyClosed";
    public const string TokenInvalid = "Error.TokenInvalid";
    public const string SessionExpired = "Error.SessionExpired";
    public const string TermsNotAccepted = "Validation.TermsNotAccepted";
    public const string EmailUnchanged = "Validation.EmailUnchanged";
    public const string AdminCannotDeleteAccount = "Error.AdminCannotDeleteAccount";
    public const string CannotBlockYourself = "Error.CannotBlockYourself";
    public const string ConversationBlocked = "Error.ConversationBlocked";
    public const string ConversationUnavailable = "Error.ConversationUnavailable";
    public const string FeaturingUnavailable = "Error.FeaturingUnavailable";
    public const string FeatureDaysInvalid = "Validation.FeatureDaysInvalid";
    public const string AdNotPublic = "Error.AdNotPublic";
    public const string ReportReasonInvalid = "Validation.ReportReasonInvalid";
    public const string ReportDetailsRequired = "Validation.ReportDetailsRequired";
    public const string ReportDetailsTooLong = "Validation.ReportDetailsTooLong";
    public const string ReportStatusInvalid = "Validation.ReportStatusInvalid";
    public const string TokenRequired = "Validation.TokenRequired";
    public const string CurrentPasswordIncorrect = "Validation.CurrentPasswordIncorrect";
    public const string CurrentPasswordRequired = "Validation.CurrentPasswordRequired";

    /// <summary>Key of the translated name of a category (see DefaultCategories).</summary>
    public static string Category(string categoryKey) => "Category." + categoryKey;
}

internal static class Messages
{
    public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Translations =
        new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["es"] = new Dictionary<string, string>
            {
                [MessageKeys.ValidationFailed] = "Hay errores de validación.",
                [MessageKeys.EmailTaken] = "Ya existe una cuenta con ese correo.",
                [MessageKeys.InvalidCredentials] = "Correo o contraseña incorrectos.",
                [MessageKeys.UserNotFound] = "Usuario no encontrado.",
                [MessageKeys.Unexpected] = "Ocurrió un error inesperado.",
                [MessageKeys.BadRequest] = "La solicitud no es válida. Revisa el formato de los datos enviados.",

                [MessageKeys.EmailRequired] = "El correo es obligatorio.",
                [MessageKeys.EmailInvalid] = "El correo no tiene un formato válido.",
                [MessageKeys.EmailTooLong] = "El correo no puede tener más de {0} caracteres.",
                [MessageKeys.PasswordRequired] = "La contraseña es obligatoria.",
                [MessageKeys.PasswordTooShort] = "La contraseña debe tener al menos {0} caracteres.",
                [MessageKeys.PasswordTooLong] = "La contraseña no puede tener más de {0} caracteres.",
                [MessageKeys.PasswordNeedsLetterAndDigit] = "La contraseña debe tener al menos una letra y un número.",
                [MessageKeys.DisplayNameRequired] = "El nombre es obligatorio.",
                [MessageKeys.DisplayNameTooLong] = "El nombre no puede tener más de {0} caracteres.",
                [MessageKeys.LanguageUnsupported] = "El idioma no es compatible. Idiomas disponibles: {0}.",
                [MessageKeys.LatitudeOutOfRange] = "La latitud debe estar entre -90 y 90.",
                [MessageKeys.LongitudeOutOfRange] = "La longitud debe estar entre -180 y 180.",
                [MessageKeys.LocationIncomplete] = "Envía la latitud y la longitud juntas, o ninguna de las dos.",
                [MessageKeys.PageInvalid] = "La página debe ser 1 o mayor.",
                [MessageKeys.PageSizeInvalid] = "El tamaño de página debe estar entre 1 y {0}.",

                [MessageKeys.Forbidden] = "No tienes permiso para hacer esto.",
                [MessageKeys.AdNotFound] = "Aviso no encontrado.",
                [MessageKeys.ImageNotFound] = "Imagen no encontrada.",
                [MessageKeys.AdRemoved] = "El aviso fue retirado por un administrador.",
                [MessageKeys.TooManyImages] = "Un aviso puede tener hasta {0} imágenes.",
                [MessageKeys.CategoryInvalid] = "La categoría no existe.",
                [MessageKeys.TitleRequired] = "El título es obligatorio.",
                [MessageKeys.TitleLength] = "El título debe tener entre {0} y {1} caracteres.",
                [MessageKeys.DescriptionRequired] = "La descripción es obligatoria.",
                [MessageKeys.DescriptionTooLong] = "La descripción no puede tener más de {0} caracteres.",
                [MessageKeys.PriceInvalid] = "El precio debe estar entre 0 y {0}.",
                [MessageKeys.CurrencyUnsupported] = "La moneda no es compatible. Monedas disponibles: {0}.",
                [MessageKeys.FileRequired] = "Envía un archivo.",
                [MessageKeys.FileTooLarge] = "El archivo no puede pesar más de {0} MB.",
                [MessageKeys.FileExtensionUnsupported] = "Tipo de archivo no permitido. Permitidos: {0}.",
                [MessageKeys.FileContentInvalid] = "El contenido del archivo no es una imagen válida (JPG, PNG o WebP).",
                [MessageKeys.SearchTextTooLong] = "El texto de búsqueda no puede tener más de {0} caracteres.",
                [MessageKeys.PriceRangeInvalid] = "El precio mínimo no puede ser mayor que el máximo, y ninguno puede ser negativo.",
                [MessageKeys.RadiusInvalid] = "El radio debe ser mayor que 0 y de máximo {0} km, y requiere latitud y longitud.",

                [MessageKeys.PhotoNotFound] = "El usuario no tiene foto de perfil.",
                [MessageKeys.ConversationNotFound] = "Conversación no encontrada.",
                [MessageKeys.AttachmentNotFound] = "Archivo no encontrado.",
                [MessageKeys.CannotMessageYourself] = "No puedes escribirte a ti mismo en tu propio aviso.",
                [MessageKeys.MessageEmpty] = "Escribe un mensaje o adjunta un archivo.",
                [MessageKeys.MessageTooLong] = "El mensaje no puede tener más de {0} caracteres.",
                [MessageKeys.TooManyFiles] = "Un mensaje puede llevar hasta {0} archivos.",
                [MessageKeys.AttachmentExtensionUnsupported] = "Tipo de archivo no permitido. Permitidos: {0}.",
                [MessageKeys.AttachmentContentInvalid] = "El contenido de \"{0}\" no corresponde a su tipo de archivo.",

                [MessageKeys.AlreadyReported] = "Ya denunciaste este aviso. Un administrador lo revisará.",
                [MessageKeys.CannotReportOwnAd] = "No puedes denunciar tu propio aviso.",
                [MessageKeys.ReportNotFound] = "Denuncia no encontrada.",
                [MessageKeys.ReportAlreadyClosed] = "Esta denuncia ya fue atendida.",
                [MessageKeys.TokenInvalid] = "El enlace no es válido o ya venció. Solicita uno nuevo.",
                [MessageKeys.SessionExpired] = "Tu sesión expiró. Inicia sesión de nuevo.",
                [MessageKeys.TermsNotAccepted] = "Debes aceptar los Términos de uso y el Aviso de privacidad para crear tu cuenta.",
                [MessageKeys.EmailUnchanged] = "El nuevo correo es el mismo que el actual.",
                [MessageKeys.AdminCannotDeleteAccount] = "Una cuenta de administrador no se puede borrar desde aquí.",
                [MessageKeys.CannotBlockYourself] = "No puedes bloquearte a ti mismo.",
                [MessageKeys.ConversationBlocked] = "No puedes escribir a este usuario.",
                [MessageKeys.ConversationUnavailable] = "Esta cuenta ya no existe, no se puede escribir.",
                [MessageKeys.FeaturingUnavailable] = "Destacar avisos no está disponible por ahora.",
                [MessageKeys.FeatureDaysInvalid] = "Elige una duración válida: {0} días.",
                [MessageKeys.AdNotPublic] = "Solo se puede destacar un aviso activo y vigente.",
                [MessageKeys.ReportReasonInvalid] = "El motivo no es válido. Motivos disponibles: {0}.",
                [MessageKeys.ReportDetailsRequired] = "Cuéntanos qué pasa para el motivo «otro».",
                [MessageKeys.ReportDetailsTooLong] = "El detalle no puede tener más de {0} caracteres.",
                [MessageKeys.ReportStatusInvalid] = "El estado no es válido. Estados disponibles: {0}.",
                [MessageKeys.TokenRequired] = "El enlace es obligatorio.",
                [MessageKeys.CurrentPasswordIncorrect] = "La contraseña actual no es correcta.",
                [MessageKeys.CurrentPasswordRequired] = "Escribe tu contraseña actual.",

                [MessageKeys.Category("vehicles")] = "Vehículos",
                [MessageKeys.Category("realEstate")] = "Inmuebles",
                [MessageKeys.Category("jobs")] = "Empleos",
                [MessageKeys.Category("services")] = "Servicios",
                [MessageKeys.Category("electronics")] = "Electrónica",
                [MessageKeys.Category("home")] = "Hogar",
                [MessageKeys.Category("fashion")] = "Moda",
                [MessageKeys.Category("pets")] = "Mascotas",
                [MessageKeys.Category("sports")] = "Deportes",
                [MessageKeys.Category("other")] = "Otros",
                [MessageKeys.Category("personalServices")] = "Servicios personales",
            },
            ["en"] = new Dictionary<string, string>
            {
                [MessageKeys.ValidationFailed] = "There are validation errors.",
                [MessageKeys.EmailTaken] = "An account with that email already exists.",
                [MessageKeys.InvalidCredentials] = "Incorrect email or password.",
                [MessageKeys.UserNotFound] = "User not found.",
                [MessageKeys.Unexpected] = "An unexpected error occurred.",
                [MessageKeys.BadRequest] = "The request is not valid. Check the format of the data you sent.",

                [MessageKeys.EmailRequired] = "Email is required.",
                [MessageKeys.EmailInvalid] = "Email does not have a valid format.",
                [MessageKeys.EmailTooLong] = "Email cannot be longer than {0} characters.",
                [MessageKeys.PasswordRequired] = "Password is required.",
                [MessageKeys.PasswordTooShort] = "Password must be at least {0} characters long.",
                [MessageKeys.PasswordTooLong] = "Password cannot be longer than {0} characters.",
                [MessageKeys.PasswordNeedsLetterAndDigit] = "Password must contain at least one letter and one number.",
                [MessageKeys.DisplayNameRequired] = "Name is required.",
                [MessageKeys.DisplayNameTooLong] = "Name cannot be longer than {0} characters.",
                [MessageKeys.LanguageUnsupported] = "The language is not supported. Available languages: {0}.",
                [MessageKeys.LatitudeOutOfRange] = "Latitude must be between -90 and 90.",
                [MessageKeys.LongitudeOutOfRange] = "Longitude must be between -180 and 180.",
                [MessageKeys.LocationIncomplete] = "Send latitude and longitude together, or neither.",
                [MessageKeys.PageInvalid] = "Page must be 1 or greater.",
                [MessageKeys.PageSizeInvalid] = "Page size must be between 1 and {0}.",

                [MessageKeys.Forbidden] = "You are not allowed to do this.",
                [MessageKeys.AdNotFound] = "Ad not found.",
                [MessageKeys.ImageNotFound] = "Image not found.",
                [MessageKeys.AdRemoved] = "The ad was removed by an administrator.",
                [MessageKeys.TooManyImages] = "An ad can have up to {0} images.",
                [MessageKeys.CategoryInvalid] = "The category does not exist.",
                [MessageKeys.TitleRequired] = "Title is required.",
                [MessageKeys.TitleLength] = "Title must be between {0} and {1} characters long.",
                [MessageKeys.DescriptionRequired] = "Description is required.",
                [MessageKeys.DescriptionTooLong] = "Description cannot be longer than {0} characters.",
                [MessageKeys.PriceInvalid] = "Price must be between 0 and {0}.",
                [MessageKeys.CurrencyUnsupported] = "The currency is not supported. Available currencies: {0}.",
                [MessageKeys.FileRequired] = "Send a file.",
                [MessageKeys.FileTooLarge] = "The file cannot be larger than {0} MB.",
                [MessageKeys.FileExtensionUnsupported] = "File type not allowed. Allowed: {0}.",
                [MessageKeys.FileContentInvalid] = "The content of the file is not a valid image (JPG, PNG or WebP).",
                [MessageKeys.SearchTextTooLong] = "The search text cannot be longer than {0} characters.",
                [MessageKeys.PriceRangeInvalid] = "The minimum price cannot be greater than the maximum, and neither can be negative.",
                [MessageKeys.RadiusInvalid] = "The radius must be greater than 0 and at most {0} km, and it requires latitude and longitude.",

                [MessageKeys.PhotoNotFound] = "The user has no profile photo.",
                [MessageKeys.ConversationNotFound] = "Conversation not found.",
                [MessageKeys.AttachmentNotFound] = "File not found.",
                [MessageKeys.CannotMessageYourself] = "You cannot write to yourself on your own ad.",
                [MessageKeys.MessageEmpty] = "Write a message or attach a file.",
                [MessageKeys.MessageTooLong] = "The message cannot be longer than {0} characters.",
                [MessageKeys.TooManyFiles] = "A message can carry up to {0} files.",
                [MessageKeys.AttachmentExtensionUnsupported] = "File type not allowed. Allowed: {0}.",
                [MessageKeys.AttachmentContentInvalid] = "The content of \"{0}\" does not match its file type.",

                [MessageKeys.AlreadyReported] = "You already reported this ad. An administrator will review it.",
                [MessageKeys.CannotReportOwnAd] = "You cannot report your own ad.",
                [MessageKeys.ReportNotFound] = "Report not found.",
                [MessageKeys.ReportAlreadyClosed] = "This report was already handled.",
                [MessageKeys.TokenInvalid] = "The link is not valid or has expired. Request a new one.",
                [MessageKeys.SessionExpired] = "Your session has expired. Sign in again.",
                [MessageKeys.TermsNotAccepted] = "You must accept the Terms of use and the Privacy notice to create your account.",
                [MessageKeys.EmailUnchanged] = "The new email is the same as the current one.",
                [MessageKeys.AdminCannotDeleteAccount] = "An administrator account cannot be deleted from here.",
                [MessageKeys.CannotBlockYourself] = "You cannot block yourself.",
                [MessageKeys.ConversationBlocked] = "You cannot write to this user.",
                [MessageKeys.ConversationUnavailable] = "This account no longer exists, so you cannot write to it.",
                [MessageKeys.FeaturingUnavailable] = "Featuring ads is not available at the moment.",
                [MessageKeys.FeatureDaysInvalid] = "Choose a valid duration: {0} days.",
                [MessageKeys.AdNotPublic] = "Only an active ad that has not expired can be featured.",
                [MessageKeys.ReportReasonInvalid] = "The reason is not valid. Available reasons: {0}.",
                [MessageKeys.ReportDetailsRequired] = "Tell us what is wrong for the reason «other».",
                [MessageKeys.ReportDetailsTooLong] = "The details cannot be longer than {0} characters.",
                [MessageKeys.ReportStatusInvalid] = "The status is not valid. Available statuses: {0}.",
                [MessageKeys.TokenRequired] = "The link is required.",
                [MessageKeys.CurrentPasswordIncorrect] = "The current password is not correct.",
                [MessageKeys.CurrentPasswordRequired] = "Enter your current password.",

                [MessageKeys.Category("vehicles")] = "Vehicles",
                [MessageKeys.Category("realEstate")] = "Real estate",
                [MessageKeys.Category("jobs")] = "Jobs",
                [MessageKeys.Category("services")] = "Services",
                [MessageKeys.Category("electronics")] = "Electronics",
                [MessageKeys.Category("home")] = "Home",
                [MessageKeys.Category("fashion")] = "Fashion",
                [MessageKeys.Category("pets")] = "Pets",
                [MessageKeys.Category("sports")] = "Sports",
                [MessageKeys.Category("other")] = "Other",
                [MessageKeys.Category("personalServices")] = "Personal services",
            }
        };
}
