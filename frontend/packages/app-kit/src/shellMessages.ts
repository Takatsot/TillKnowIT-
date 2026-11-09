import { translateArea, type AreaMessages, type LanguageCode } from "@tillknowit/i18n";

export type ShellKey =
  | "app" | "language" | "missing" | "noSections"
  | "signInTitle" | "phone" | "phoneHint" | "sendCode" | "code" | "codeHint" | "signInButton" | "differentNumber" | "devOnly" | "devCode"
  | "otp.phone_invalid" | "otp.too_many" | "otp.invalid" | "codeSent"
  | "sessionEnded" | "sessionExpired"
  | "signedInAs" | "signOut" | "workingFor" | "workingForNone" | "membershipsFailed" | "orgShop" | "orgSupplier" | "orgOther"
  | "advanced" | "advancedHint" | "shopId" | "supplierId" | "deviceId" | "save"
  | "newHere" | "haveAccount" | "yourName" | "yourNameHint" | "heroBefore" | "heroEmphasis" | "heroLead" | "heroPoint1" | "heroPoint2" | "heroPoint3";

export const shellMessages: AreaMessages<ShellKey> = {
  en: {
    app: "TillKnowIT", language: "Language", missing: "Fill in {what} to use this part.", noSections: "Nothing is available yet.",
    signInTitle: "Sign in", phone: "Your cell phone number", phoneHint: "For example 082 123 4567. We will send you a code.", sendCode: "Send me a code",
    code: "The code we sent you", codeHint: "Check your messages for the code.", signInButton: "Sign in", differentNumber: "Use a different number",
    devOnly: "Development only", devCode: "The server sent the code back so you can test without a phone: {code}. This never happens in the real service.",
    "otp.phone_invalid": "That does not look like a cell phone number. Check it and try again.",
    "otp.too_many": "Too many tries. Please wait a few minutes and try again.",
    "otp.invalid": "That code is not right, or it has expired. Check it or ask for a new code.",
    codeSent: "If that number has an account, a code is on its way.",
    sessionEnded: "Please sign in again.", sessionExpired: "Your sign-in has run out. Please sign in again.",
    signedInAs: "Signed in as", signOut: "Sign out", workingFor: "Who are you working for?", workingForNone: "You are not part of any shop or supplier yet.",
    membershipsFailed: "We could not load your shops and suppliers.", orgShop: "shop", orgSupplier: "supplier", orgOther: "organisation",
    advanced: "Advanced: type ids by hand", advancedHint: "Only for testing. Normally the shop and supplier come from who you work for above.",
    shopId: "Shop id", supplierId: "Supplier organisation id", deviceId: "Device id", save: "Save",
    newHere: "I am new here: create an account", haveAccount: "I already have an account", yourName: "Your name", yourNameHint: "As the people you work with know you.",
    heroBefore: "Run your shop with", heroEmphasis: "confidence", heroLead: "Sales, stock, suppliers and customers, in one calm place. It keeps working when the data runs out, too.",
    heroPoint1: "Sell offline, sync when you are back", heroPoint2: "Know what to reorder, and from whom", heroPoint3: "Built for spaza shops and tuckshops",
  },
  // DRAFT translations of the sign-in screen and menu words, written without a first-language speaker: they must be reviewed before release. The rest shows English.
  translations: {
    af: { "language": "Taal", "signInTitle": "Meld aan", "phone": "Jou selfoonnommer", "phoneHint": "Byvoorbeeld 082 123 4567. Ons stuur vir jou 'n kode.", "sendCode": "Stuur vir my 'n kode", "code": "Die kode wat ons gestuur het", "codeHint": "Kyk in jou boodskappe vir die kode.", "signInButton": "Meld aan", "differentNumber": "Gebruik 'n ander nommer", "otp.phone_invalid": "Dit lyk nie soos 'n selfoonnommer nie. Kyk dit na en probeer weer.", "otp.too_many": "Te veel pogings. Wag asseblief 'n paar minute en probeer weer.", "otp.invalid": "Daardie kode is nie reg nie, of dit het verval. Kyk dit na of vra vir 'n nuwe kode.", "codeSent": "As daardie nommer 'n rekening het, is 'n kode op pad.", "sessionEnded": "Meld asseblief weer aan.", "sessionExpired": "Jou aanmelding het verval. Meld asseblief weer aan.", "signedInAs": "Aangemeld as", "signOut": "Meld af", "workingFor": "Vir wie werk jy?" },
    zu: { "language": "Ulimi", "signInTitle": "Ngena", "phone": "Inombolo yakho yocingo", "phoneHint": "Isibonelo 082 123 4567. Sizokuthumelela ikhodi.", "sendCode": "Ngithumelele ikhodi", "code": "Ikhodi esikuthumelele yona", "codeHint": "Bheka imilayezo yakho ukuthola ikhodi.", "signInButton": "Ngena", "differentNumber": "Sebenzisa enye inombolo", "otp.phone_invalid": "Lokhu akufani nenombolo yocingo. Hlola futhi uzame futhi.", "otp.too_many": "Imizamo eminingi kakhulu. Sicela ulinde imizuzu embalwa uzame futhi.", "otp.invalid": "Leyo khodi ayilungile, noma iphelelwe yisikhathi. Hlola noma ucele enye ikhodi.", "codeSent": "Uma leyo nombolo ineakhawunti, ikhodi isendleleni.", "sessionEnded": "Sicela ungene futhi.", "sessionExpired": "Ukungena kwakho kuphelile. Sicela ungene futhi.", "signedInAs": "Ungene njengo", "signOut": "Phuma", "workingFor": "Usebenzela bani?" },
    xh: { "language": "Ulwimi", "signInTitle": "Ngena", "phone": "Inombolo yakho yomnxeba", "phoneHint": "Umzekelo 082 123 4567. Siza kukuthumela ikhowudi.", "sendCode": "Ndithumelele ikhowudi", "code": "Ikhowudi esiyithumele kuwe", "codeHint": "Jonga imiyalezo yakho ukufumana ikhowudi.", "signInButton": "Ngena", "differentNumber": "Sebenzisa enye inombolo", "otp.phone_invalid": "Oku akufani nenombolo yomnxeba. Khangela uphinde uzame.", "otp.too_many": "Iinzame ezininzi kakhulu. Nceda ulinde imizuzu embalwa uphinde uzame.", "otp.invalid": "Loo khowudi ayichanekanga, okanye iphelelwe lixesha. Khangela okanye ucele enye ikhowudi.", "codeSent": "Ukuba loo nombolo inaye iakhawunti, ikhowudi isendleleni.", "sessionEnded": "Nceda ungene kwakhona.", "sessionExpired": "Ukungena kwakho kuphelile. Nceda ungene kwakhona.", "signedInAs": "Ungene njengo", "signOut": "Phuma", "workingFor": "Usebenzela bani?" },
    st: { "language": "Puo", "signInTitle": "Kena", "phone": "Nomoro ea hau ea mohala", "phoneHint": "Mohlala 082 123 4567. Re tla u romella khoutu.", "sendCode": "Nthomelle khoutu", "code": "Khoutu eo re u rometseng eona", "codeHint": "Sheba melaetsa ea hau bakeng sa khoutu.", "signInButton": "Kena", "differentNumber": "Sebelisa nomoro e ngoe", "otp.phone_invalid": "Sena ha se shebahale joaloka nomoro ea mohala. Sheba 'me u leke hape.", "otp.too_many": "Liteko tse ngata haholo. Ka kopo ema metsotso e seng mekae 'me u leke hape.", "otp.invalid": "Khoutu eo ha e nepahale, kapa e fetile ke nako. E sheba kapa u kope e ncha.", "codeSent": "Haeba nomoro eo e na le akhaonto, khoutu e tseleng.", "sessionEnded": "Ka kopo kena hape.", "sessionExpired": "Ho kena ha hau ho fedile. Ka kopo kena hape.", "signedInAs": "O kene e le", "signOut": "Tsoa", "workingFor": "O sebeletsa mang?" },
    tn: { "language": "Puo", "signInTitle": "Tsena", "phone": "Nomoro ya gago ya mogala", "phoneHint": "Sekai 082 123 4567. Re tla go romela khoutu.", "sendCode": "Nthomele khoutu", "code": "Khoutu e re e go rometseng", "codeHint": "Lebelela melaetsa ya gago go bona khoutu.", "signInButton": "Tsena", "differentNumber": "Dirisa nomoro e nngwe", "otp.phone_invalid": "Seno ga se tshwane le nomoro ya mogala. Tlhola gape o leke gape.", "otp.too_many": "Dikgato tse dintsi thata. Tsweetswee emela metsotso e le mmalwa o leke gape.", "otp.invalid": "Khoutu eo ga e a nepagala, kgotsa e fetile ka nako. E tlhole kgotsa o kope e nngwe.", "codeSent": "Fa nomoro eo e na le akhaonto, khoutu e mo tseleng.", "sessionEnded": "Tsweetswee tsena gape.", "sessionExpired": "Go tsena ga gago go fedile. Tsweetswee tsena gape.", "signedInAs": "O tsene jaaka", "signOut": "Tswa", "workingFor": "O berekela mang?" },
  },
};

export const shellText = (language: LanguageCode, key: ShellKey, params?: Record<string, string | number>) => translateArea(shellMessages, language, key, params);
