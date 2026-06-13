# http-handler

The inbound HTTP **verb routers** (`GET`/`POST`/`PUT`/`DELETE`/`PATCH`/`HEAD`),
delivered as an attach-mode package (decision 20.3). The base of the HTTP API.

Each verb maps a request path to a backtick sub-attribute
(`GET /http/foo/bar` → `` GET`FOO`BAR ``), decodes the query string into
`%q<form.*>`, and answers a clean `404 API NOT FOUND` for unrouted paths. It
manages only these attributes on the configured `http_handler` object
(`{{$http_handler}}`, #4) — it never creates or destroys the object.

Feature packages add routes as backtick children of these verbs and depend on
this package — see [`profile-handler`](../profile-handler/). Installing
`http-handler` alone gives you a working router with no routes yet; uninstall
it and the handler object keeps its other softcode.
