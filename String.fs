module String

open System
open System.Globalization

let private sureNonNull : string -> string = function null -> "" | s -> s

let replace (from : string, ``to`` : string) (s : string) =
    (sureNonNull s).Replace(from, ``to``, StringComparison.InvariantCulture)

let toUpper (s : string) = (sureNonNull s).ToUpper(CultureInfo.InvariantCulture)
