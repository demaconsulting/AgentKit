# Release Process

This is how the team ships a new version of the product. It is short on purpose: the document
assistant sample asks an agent to turn it into a flowchart.

## Prepare

1. Create a release branch from `main`, named after the version, for example `release/2.4`.
2. Update the change log with every user-visible change since the last release.

## Verify

1. Run the full build and test suite on the release branch.
2. If any test fails, fix it on `main`, merge the fix into the release branch, and run the suite
   again.
3. When the suite passes, ask a second engineer to review the change log.

## Publish

1. Tag the release branch with the version, for example `v2.4.0`.
2. Publish the packages from the tagged build.
3. Announce the release to users with a link to the change log.

## After the Release

1. Merge the release branch back into `main`.
2. Close the milestone and move any unfinished items to the next one.
