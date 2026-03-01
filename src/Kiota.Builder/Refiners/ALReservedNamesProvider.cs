using System;
using System.Collections.Generic;

namespace Kiota.Builder.Refiners;

public class ALReservedNamesProvider : IReservedNamesProvider
{
    private readonly Lazy<HashSet<string>> _reservedNames = new(() => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "area", "action", "actions", "any", "auditcategory",
        "begin", "biginteger", "bigtext", "blob", "boolean", "break", "byte",
        "case", "char", "clienttype", "code", "codeunit", "commitbehavior",
        "companyproperty", "continue", "cookie",
        "database", "dataclassification", "datascope", "datatransfer", "date",
        "dateformula", "datetime", "debugger", "decimal", "defaultlayout",
        "dialog", "dictionary", "do", "dotnet", "duration",
        "else", "end", "enum", "errorbehavior", "errorinfo", "errortype",
        "executioncontext", "executionmode", "exit",
        "fieldclass", "fieldref", "fieldtype", "file", "fileupload",
        "filterpagebuilder", "for",
        "guid",
        "httpclient", "httpcontent", "httpheaders", "httprequestmessage",
        "httprequesttype", "httpresponsemessage",
        "if", "inherentpermissionsscope", "instream", "integer", "interface",
        "internal", "isolatedstorage", "isolationlevel", "item_idx",
        "jsonarray", "jsonobject", "jsontoken", "jsonvalue",
        "key", "keyref",
        "label", "list", "local",
        "media", "mediaset", "moduledependencyinfo", "moduleinfo",
        "navapp", "none", "notification", "notificationscope", "numbersequence",
        "objecttype", "option", "outstream",
        "page", "pagebackgroundtaskerrorlevel", "pagestyle",
        "permissionobjecttype", "procedure", "productname", "promptmode",
        "query",
        "record", "recordid", "recordref", "repeat", "report", "reportformat",
        "reportlayouttype", "requestpage", "run",
        "secrettext", "securityfilter", "securityoperationresult", "session",
        "sessioninformation", "sessionsettings", "system",
        "table", "tableconnectiontype", "taskscheduler", "telemetryscope",
        "testaction", "testfield", "testfilter", "testfilterfield",
        "testhttprequestmessage", "testhttpresponsemessage", "testpage",
        "testpart", "testpermissions", "testrequestpage", "text", "textbuilder",
        "textconst", "textencoding", "then", "time", "to", "transactionmodel",
        "transactiontype", "trigger",
        "until",
        "var", "variant", "verbosity", "version",
        "webserviceactioncontext", "webserviceactionresultcode", "while",
        "xmlattribute", "xmlattributecollection", "xmlcdata", "xmlcomment",
        "xmldeclaration", "xmldocument", "xmldocumenttype", "xmlelement",
        "xmlmport", "xmlnamespacemanager", "xmlnametable", "xmlnode",
        "xmlnodelist", "xmlport", "xmlprocessinginstruction", "xmlreadoptions",
        "xmltext", "xmlwriteoptions",
    });

    public HashSet<string> ReservedNames => _reservedNames.Value;

    public static string GetSafeName(string name)
    {
        var provider = new ALReservedNamesProvider();
        if (provider.ReservedNames.Contains(name))
            return name + "_";
        return name;
    }
}
