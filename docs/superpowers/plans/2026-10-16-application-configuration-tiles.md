# Application configuration tiles

Add a private configuration tile catalog using the existing application registry and loaded plugin application sources. Tiles carry display metadata and a local destination only; configuration values stay behind each application's existing authorized endpoint. Discord contributes a link to its existing admin page.

1. Add API regression coverage for permission filtering, unsafe routes, absent plugins, and persisted registrations. Add configuration home coverage for contributed links and failed reads; observe the missing-feature failures.
2. Add nullable metadata to registered applications, persistence and administrative registration. Extend IApplicationSource with a default empty contribution method, retaining its existing load-once lifecycle.
3. Add a config.admin endpoint that merges registered and loaded plugin tiles, checks each permission server-side, validates destinations, and isolates throwing plugin sources.
4. Fetch the private catalog independently on the configuration home. Retain core cards and show failed application reads. Add Discord's declarative contribution and contributor documentation.
5. Run focused and broader component/controller tests, formatting, plugin build/tests, and independent review before the PR.
