"""覆盖默认选择、显式回归及防止专项误充发布结果的边界。"""
from pathlib import Path
import copy
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from test_plan import select_plan, validate_result, validate_release_result

class TestSelection(unittest.TestCase):
    def test_default_only_current_and_related(self):
        config = {'CurrentProfile': 'example', 'Profiles': {'example': {'Primary': ['new-change'], 'Related': ['startup'], 'DefaultMode': 'full', 'Description': 'test'}}}
        with patch('test_plan.read', return_value=config):
            plan = select_plan()
        self.assertEqual((plan['Suite'], plan['Profile'], plan['Mode']), ('targeted', 'example', 'full'))
        self.assertEqual(plan['RequestedSuites'], ['startup', 'new-change'])

    def test_full_environment_does_not_select_full_regression(self):
        plan = select_plan(mode='full')
        self.assertEqual(plan['Suite'], 'targeted')
        self.assertEqual(plan['RequestedSuites'], select_plan()['RequestedSuites'])
        self.assertNotIn('legacy-regression', plan['RequestedSuites'])

    def test_previous_art_profile_keeps_minimal_environment(self):
        self.assertEqual(select_plan(profile='modform-art')['Mode'], 'minimal')

    def test_explicit_minimal_environment_overrides_profile(self):
        self.assertEqual(select_plan(mode='minimal')['Mode'], 'minimal')

    def test_regression_must_be_explicit(self):
        self.assertEqual(select_plan(suite='regression')['RequestedSuites'], ['legacy-regression'])
        self.assertEqual(select_plan(suite='regression')['Profile'], 'all')

    def test_explicit_related_suite(self):
        self.assertEqual(select_plan(profile='fusion-upgrades')['RequestedSuites'], ['startup', 'upgrade-preview', 'fusion-upgrades'])

    def test_unknown_profile_rejected(self):
        with self.assertRaises(ValueError):
            select_plan(profile='typo')

    def test_regression_profile_cannot_silently_ignore_filter(self):
        with self.assertRaises(ValueError):
            select_plan(suite='regression', profile='modform-art')

    def test_unknown_suite_or_environment_rejected(self):
        for args in ({'suite': 'typo'}, {'mode': 'typo'}):
            with self.subTest(args=args), self.assertRaises(ValueError):
                select_plan(**args)

class TestResultGate(unittest.TestCase):
    def setUp(self):
        self.request = dict(select_plan(mode='minimal'), RunId='test-run', RunnerSha256='runner', AssemblyHash='dll', ProbeHash='probe')
        self.result = dict(self.request, ExecutedSuites=self.request['RequestedSuites'], RegressionComplete=False,
                           Passed=1, Failed=0, Checks=['native art check'], Failures=[])

    def regression(self):
        result = dict(self.result, Suite='regression', Profile='all', RequestedSuites=['legacy-regression'],
                      ExecutedSuites=['legacy-regression'], RegressionComplete=True)
        return result

    def test_valid_targeted_result_accepted_for_its_request(self):
        validate_result(self.result, self.request)

    def test_targeted_result_rejected_as_release(self):
        with self.assertRaises(ValueError):
            validate_release_result(self.result, 'minimal', 'dll', 'probe', 'runner')

    def test_valid_explicit_regression_accepted(self):
        validate_release_result(self.regression(), 'minimal', 'dll', 'probe', 'runner')

    def test_stale_or_wrong_environment_release_rejected(self):
        for mode, dll, probe, runner in [('full', 'dll', 'probe', 'runner'), ('minimal', 'old', 'probe', 'runner'),
                                          ('minimal', 'dll', 'old', 'runner'), ('minimal', 'dll', 'probe', 'old')]:
            with self.subTest(mode=mode, dll=dll, probe=probe, runner=runner), self.assertRaises(ValueError):
                validate_release_result(self.regression(), mode, dll, probe, runner)

    def test_historical_unscoped_result_rejected(self):
        with self.assertRaises(ValueError):
            validate_release_result(dict(Passed=1249, Failed=0, AssemblyHash='dll'), 'full', 'dll', 'probe', 'runner')

    def test_missing_or_inconsistent_result_fields_rejected(self):
        for key, value in [('RunId', 'other'), ('ExecutedSuites', ['startup']), ('Failed', None),
                           ('Failed', 1), ('Passed', 0), ('Passed', 2), ('Failures', ['failed']),
                           ('RegressionComplete', True), ('RequestedSuites', ['legacy-regression'])]:
            result = copy.deepcopy(self.result)
            result[key] = value
            with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                validate_result(result, self.request)

    def test_partial_regression_cannot_pass_release(self):
        for key, value in [('ExecutedSuites', []), ('RegressionComplete', False), ('RunId', ''), ('Failed', None)]:
            result = self.regression()
            result[key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                validate_release_result(result, 'minimal', 'dll', 'probe', 'runner')

if __name__ == '__main__':
    unittest.main()
