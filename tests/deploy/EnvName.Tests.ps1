# Unit tests of the pure helpers of the deploy module. Run: pwsh -NoProfile -c "Invoke-Pester tests/deploy -Output Detailed"

BeforeAll {
    Import-Module "$PSScriptRoot/../../deploy/lib/EphemeralEnvs.Deploy.psm1" -Force
}

Describe 'Get-EnvName -Branch' {
    It 'lowercases and replaces every non [a-z0-9] run with one dash' {
        Get-EnvName -Branch 'my/Add-X_2' | Should -Be 'my-add-x-2'
    }

    It 'trims leading and trailing dashes' {
        Get-EnvName -Branch '--Fix//bug--' | Should -Be 'fix-bug'
    }

    It 'keeps a name that is already normal' {
        Get-EnvName -Branch 'master' | Should -Be 'master'
    }

    It 'drops the branch type prefix <Prefix>/' -ForEach @(
        @{ Prefix = 'feature' }, @{ Prefix = 'bugfix' }, @{ Prefix = 'hotfix' }, @{ Prefix = 'fix' },
        @{ Prefix = 'story' }, @{ Prefix = 'chore' }, @{ Prefix = 'Feature' }
    ) {
        Get-EnvName -Branch "$Prefix/12615-ai-chat-block" | Should -Be '12615-ai-chat-block'
    }

    It 'keeps a prefix that is not a branch type, or not at the start' {
        Get-EnvName -Branch 'features/x' | Should -Be 'features-x'
        Get-EnvName -Branch 'my/feature/x' | Should -Be 'my-feature-x'
    }

    It 'keeps a name of exactly 30 chars' {
        Get-EnvName -Branch ('a' * 30) | Should -Be ('a' * 30)
    }

    It 'shortens a long name at a word boundary + dash + 4 hex' {
        Get-EnvName -Branch 'feature/14074-introduce-theme-specific-navigation' |
            Should -Match '^14074-introduce-theme-[0-9a-f]{4}$'
    }

    It 'cuts a long single word at 25 chars' {
        $name = Get-EnvName -Branch ('a' * 50)
        $name.Length | Should -Be 30
        $name | Should -Match '^a{25}-[0-9a-f]{4}$'
    }

    It 'keeps a whole word that ends exactly at the cut' {
        Get-EnvName -Branch (('a' * 25) + '-tail-words') | Should -Match '^a{25}-[0-9a-f]{4}$'
        Get-EnvName -Branch (('a' * 24) + '-b' * 10) | Should -Match '^a{24}-[0-9a-f]{4}$'
    }

    It 'gives the same short name for the same branch' {
        Get-EnvName -Branch ('a' * 50) | Should -Be (Get-EnvName -Branch ('a' * 50))
    }

    It 'gives different short names to long branches that differ only at the end' {
        Get-EnvName -Branch 'feature/983-key-date-collection-component' |
            Should -Not -Be (Get-EnvName -Branch 'feature/983-key-date-collection-component-dev-integration')
    }

    It 'rejects a branch that normalizes to an empty name' {
        { Get-EnvName -Branch '///' } | Should -Throw '*empty*'
        { Get-EnvName -Branch 'feature/' } | Should -Throw '*empty*'
    }

    It 'rejects the reserved name platform' {
        { Get-EnvName -Branch 'platform' } | Should -Throw '*reserved*'
        { Get-EnvName -Branch 'chore/platform' } | Should -Throw '*reserved*'
    }
}

Describe 'Get-EnvName -Env' {
    It 'accepts a name in normal form' {
        Get-EnvName -Env 'feature-x' | Should -Be 'feature-x'
    }

    It 'rejects a name that is not in normal form and suggests the normal one' {
        { Get-EnvName -Env 'Feature_X' } | Should -Throw "*use 'feature-x'*"
        { Get-EnvName -Env 'feature/x' } | Should -Throw "*use 'x'*"
    }

    It 'rejects the reserved name platform' {
        { Get-EnvName -Env 'platform' } | Should -Throw '*reserved*'
    }
}

Describe 'ConvertTo-DbName' {
    It 'prefixes ee_ and replaces dashes with underscores' {
        ConvertTo-DbName -EnvName 'feature-x' | Should -Be 'ee_feature_x'
    }
}

Describe 'New-DbPassword' {
    It 'returns 32 lowercase hex chars' {
        New-DbPassword | Should -Match '^[0-9a-f]{32}$'
    }

    It 'returns a new password every time' {
        New-DbPassword | Should -Not -Be (New-DbPassword)
    }
}
