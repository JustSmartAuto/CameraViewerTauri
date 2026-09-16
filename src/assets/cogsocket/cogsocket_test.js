
define(function (require, exports, module) {
    var CogSocket = require("cogsocket");

    var cogsock;

    function printMsg(msg) {
        console.log(msg);
        if (outputText.innerText.length > 0)
            outputText.innerText += "\r\n";
        outputText.innerText += msg;
        outputText.scrollTop = outputText.scrollHeight;
    }

    function connect(url, outputText, onOpenHandler) {
        close();

        outputText.innerText = "";


        try {
            printMsg("Creating new CogSocket(\"" + url + "\")");
            cogsock = new CogSocket(new WebSocket(url), null, 2);
            cogsock.log = printMsg;
            cogsock.onopen = function () {
                printMsg("CogSocket.onopen");
                if (onOpenHandler) {
                  onOpenHandler();
                }
            };
            cogsock.onclose = function () {
                printMsg("CogSocket.onclose");
            };
            cogsock.onerror = function (err) {
                printMsg("CogSocket.onerror " + (err ? err : ""));
            };

        } catch (ex) {
            printMsg("EXCEPTION: " + ex)
        }
    }
    exports.connect = connect;

    function close() {
        if (cogsock) {
            cogsock.close();
            cogsock = undefined;
        }
    }
    exports.close = close;

    function sendRequest(dest, type, path, body, onComplete) {
        if (!cogsock) {
            dest.innerText = "NOT CONNECTED";
            if (onComplete) {
              onComplete(null);
            }
            return;
        }
        dest.innerText = "...";
        try {
            var bodyObj = (body !== undefined && body !== "") ? JSON.parse(body) : undefined;
            cogsock[type](path, bodyObj, function (resp) {
                if (resp === undefined)
                    dest.innerText = "OK";
                else if (resp instanceof Error)
                    dest.innerText = "Error: " + resp.message;
                else
                    dest.innerText = JSON.stringify(resp, null, 2);

                if (onComplete) {
                  onComplete(resp);
                }
            });
        }
        catch (ex) {
            dest.innerText = "EXCEPTION: " + ex;
            if (onComplete) {
              onComplete(ex);
            }
        }
    }

    exports.get = function(dest, path) {
        sendRequest(dest, "get", path);
    };

    exports.put = function(dest, path, body) {
        sendRequest(dest, "put", path, body);
    };

    exports.post = function(dest, path, body, onComplete) {
        sendRequest(dest, "post", path, body, onComplete);
    };

    exports.addListener = function(dest, path, onEvent) {
        if (!cogsock) {
            dest.innerText = "NOT CONNECTED";
            return;
        }
        dest.innerText = "...";
        try {
            cogsock.addListener(path, function () {
                var msg = "Got event: " + path;
                if (arguments.length > 0) {
                    msg += "(";
                    for (var a = 0; a < arguments.length; ++a) {
                        if (a > 0)
                            msg += ',';
                        msg += arguments[a];
                    }
                    msg += ")";
                }

                printMsg(msg);
                if (onEvent) {
                  onEvent(arguments);
                }
            },
            function(resp) {
                if (resp instanceof Error)
                    dest.innerText = "Error: " + resp.message;
                else
                    dest.innerText = "OK";
            });
        }
        catch (ex) {
            dest.innerText = "EXCEPTION: " + ex;
        }
    }

    exports.removeListener = function(dest, path) {
        if (!cogsock) {
            dest.innerText = "NOT CONNECTED";
            return;
        }
        dest.innerText = "...";
        try {
            cogsock.removeListener(path, undefined, function(resp) {
                if (resp instanceof Error)
                    dest.innerText = "Error: " + resp.message;
                else
                    dest.innerText = "OK";
            });
        }
        catch (ex) {
            dest.innerText = "EXCEPTION: " + ex;
        }
    }
 });