# Changesets

Every pull request that changes a published package (`@dovepeak/identity`) adds a changeset describing the change
and its semantic version impact:

```bash
npm run changeset
```

On `main`, the release workflow opens a "Version packages" pull request that applies pending changesets (version
bump and `CHANGELOG.md`). Merging it publishes to npm with provenance. The .NET SDK is versioned in its project file
and published by pushing a `dotnet-sdk-v<version>` tag.
